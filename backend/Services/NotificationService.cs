using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

/// <summary>Per-message sender override for email; any null part falls back to the SMTP settings.</summary>
public sealed record MailSender(string? FromAddress, string? FromName, string? ReplyTo);

public interface INotificationService
{
    Task NotifyAsync(
        string channel,
        string recipient,
        string subject,
        string body,
        string? idempotencyKey = null,
        Guid? locationId = null,
        MailSender? sender = null,
        string? link = null,
        string? qrText = null);
    /// <summary>With <paramref name="sender"/>, the test goes out from that address (checks the Send As permission).</summary>
    Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null, MailSender? sender = null);
    /// <summary>Re-attempts delivery for unsent Email outbox rows (e.g. after SMTP config fix).</summary>
    Task<(int Attempted, int Sent)> RetryUnsentEmailsAsync(int take = 20);
    /// <summary>
    /// Re-attempts unsent Email rows created in the window, oldest first. After a failure the remaining rows for
    /// the same tenant and location wait for the next sweep, so a broken SMTP server is not hammered.
    /// </summary>
    Task<(int Attempted, int Sent)> RetryFailedEmailsAsync(DateTime createdAfterUtc, DateTime createdBeforeUtc, int take, CancellationToken ct = default);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly ITenantContext _tenant;
    private readonly IEnumerable<INotificationDeliveryProvider> _providers;

    public NotificationService(
        ApplicationDbContext db,
        ILogger<NotificationService> logger,
        ITenantContext tenant,
        IEnumerable<INotificationDeliveryProvider> providers)
    {
        _db = db;
        _logger = logger;
        _tenant = tenant;
        _providers = providers;
    }

    private Guid QueueingTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required to queue a notification.");

    public async Task NotifyAsync(
        string channel,
        string recipient,
        string subject,
        string body,
        string? idempotencyKey = null,
        Guid? locationId = null,
        MailSender? sender = null,
        string? link = null,
        string? qrText = null)
    {
        var tenantId = QueueingTenantId;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.NotificationOutbox.IgnoreQueryFilters()
                .AnyAsync(n => n.TenantId == tenantId && n.IdempotencyKey == idempotencyKey);
            if (exists)
            {
                _logger.LogDebug("Skipping duplicate notification {Key}", idempotencyKey);
                return;
            }
        }

        var row = new NotificationOutbox
        {
            TenantId = tenantId,
            LocationId = locationId,
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            FromAddress = Clip(sender?.FromAddress, 256),
            FromName = Clip(sender?.FromName, 200),
            ReplyTo = Clip(sender?.ReplyTo, 256),
            Link = Clip(link, 300),
            QrText = Clip(qrText, 100),
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _db.Entry(row).State = EntityState.Detached;
            _logger.LogDebug("Idempotent notification already queued: {Key}", idempotencyKey);
            return;
        }

        await TryDeliverAsync(row);
    }

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    public async Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null, MailSender? sender = null)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return (false, "Recipient email is required.");

        var row = new NotificationOutbox
        {
            TenantId = QueueingTenantId,
            LocationId = locationId,
            Channel = "Email",
            Recipient = recipient.Trim(),
            Subject = SmtpTestSubject,
            Body = $"SMTP test from Tiaano Visitor Management at {AppTime.Now:dd MMM yyyy HH:mm:ss}.",
            FromAddress = Clip(sender?.FromAddress, 256),
            FromName = Clip(sender?.FromName, 200),
            ReplyTo = Clip(sender?.ReplyTo, 256),
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        await _db.SaveChangesAsync();

        var email = _providers.OfType<IEmailNotificationProvider>().FirstOrDefault();
        if (email is null)
            return (false, "Email delivery provider is not registered.");

        try
        {
            await email.DeliverStrictAsync(row);
            row.IsSent = true;
            row.SentAt = DateTime.UtcNow;
            row.Error = null;
            await _db.SaveChangesAsync();
            return (true, row.FromAddress is null
                ? $"Email sent to {row.Recipient}."
                : $"Email sent to {row.Recipient} from {row.FromAddress}.");
        }
        catch (Exception ex)
        {
            var detail = ex.GetBaseException().Message;
            row.IsSent = false;
            row.Error = detail.Length > 480 ? detail[..480] : detail;
            await _db.SaveChangesAsync();
            _logger.LogError(ex, "SMTP test failed for {Recipient}", row.Recipient);
            return (false, $"SMTP failed: {detail}");
        }
    }

    public async Task<(int Attempted, int Sent)> RetryUnsentEmailsAsync(int take = 20)
    {
        take = Math.Clamp(take, 1, 100);
        var pending = await _db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.Channel == "Email" && !n.IsSent)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();

        var sent = 0;
        foreach (var row in pending)
        {
            if (row.TenantId is not Guid tenantId || tenantId == Guid.Empty) continue;
            _tenant.Set(tenantId);
            await TryDeliverAsync(row);
            if (row.IsSent) sent++;
        }

        return (pending.Count, sent);
    }

    public async Task<(int Attempted, int Sent)> RetryFailedEmailsAsync(
        DateTime createdAfterUtc, DateTime createdBeforeUtc, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        // A failed SMTP test was shown to the person testing; sending it hours later would only confuse them.
        var pending = await _db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.Channel == "Email" && !n.IsSent
                && n.CreatedAt >= createdAfterUtc && n.CreatedAt <= createdBeforeUtc
                && n.Subject != SmtpTestSubject)
            .OrderBy(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var failedServers = new HashSet<(Guid, Guid?)>();
        var attempted = 0;
        var sent = 0;
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (row.TenantId is not Guid tenantId || tenantId == Guid.Empty) continue;
            if (failedServers.Contains((tenantId, row.LocationId))) continue;
            _tenant.Set(tenantId);
            attempted++;
            await TryDeliverAsync(row);
            if (row.IsSent) sent++;
            else failedServers.Add((tenantId, row.LocationId));
        }

        return (attempted, sent);
    }

    private const string SmtpTestSubject = "Tiaano VMS SMTP test";

    private async Task TryDeliverAsync(NotificationOutbox row)
    {
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.Channel, row.Channel, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            _logger.LogInformation(
                "Notification queued via {Channel} to {Recipient}: {Subject}",
                row.Channel,
                row.Recipient,
                row.Subject);
            return;
        }

        try
        {
            await provider.DeliverAsync(row);
            row.IsSent = true;
            row.SentAt = DateTime.UtcNow;
            row.Error = null;
            // The QR is a check-in credential; once it is in the visitor's mailbox the outbox has no use for it.
            row.QrText = null;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            row.IsSent = false;
            var detail = ex.GetBaseException().Message;
            row.Error = detail.Length > 480 ? detail[..480] : detail;
            await _db.SaveChangesAsync();
            _logger.LogError(ex, "Failed to deliver {Channel} to {Recipient}", row.Channel, row.Recipient);
        }
    }
}
