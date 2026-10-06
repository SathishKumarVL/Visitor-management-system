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
        MailSender? sender = null);
    Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null);
    /// <summary>Re-attempts delivery for unsent Email outbox rows (e.g. after SMTP config fix).</summary>
    Task<(int Attempted, int Sent)> RetryUnsentEmailsAsync(int take = 20);
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
        MailSender? sender = null)
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

    public async Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return (false, "Recipient email is required.");

        var row = new NotificationOutbox
        {
            TenantId = QueueingTenantId,
            LocationId = locationId,
            Channel = "Email",
            Recipient = recipient.Trim(),
            Subject = "Tiaano VMS SMTP test",
            Body = $"SMTP test from Tiaano Visitor Management at {AppTime.Now:dd MMM yyyy HH:mm:ss}.",
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
            return (true, $"Email sent to {row.Recipient}.");
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
