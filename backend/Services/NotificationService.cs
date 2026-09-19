using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface INotificationService
{
    Task NotifyAsync(string channel, string recipient, string subject, string body, string? idempotencyKey = null);
    Task SendVisitorThankYouEmailAsync(string visitorName, string? email, DateTime visitDateTimeLocal);
    Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient);
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
        string? idempotencyKey = null)
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
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Concurrent workers raced on the unique (TenantId, IdempotencyKey) index.
            _db.Entry(row).State = EntityState.Detached;
            _logger.LogDebug("Idempotent notification already queued: {Key}", idempotencyKey);
            return;
        }

        await TryDeliverAsync(row);
    }

    public async Task SendVisitorThankYouEmailAsync(string visitorName, string? email, DateTime visitDateTimeLocal)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Checkout thank-you email skipped — visitor {Name} has no email.", visitorName);
            return;
        }

        var when = visitDateTimeLocal.ToString("MMM dd, yyyy hh:mm tt");
        var subject = "Thank you for visiting Tiaano";
        var body = BuildThankYouBody(visitorName.Trim(), when);
        await NotifyAsync("Email", email.Trim(), subject, body);
    }

    public async Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return (false, "Recipient email is required.");

        var row = new NotificationOutbox
        {
            TenantId = QueueingTenantId,
            Channel = "Email",
            Recipient = recipient.Trim(),
            Subject = "Tiaano VMS SMTP test",
            Body = $"SMTP test from Tiaano Visitor Management at {DateTime.Now:dd MMM yyyy HH:mm:ss}.",
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        await _db.SaveChangesAsync();
        await TryDeliverAsync(row);
        return row.IsSent
            ? (true, $"Email sent to {row.Recipient}.")
            : (false, row.Error ?? "Send failed.");
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

    private static string BuildThankYouBody(string visitorName, string visitWhen) =>
        $"""
        Dear {visitorName},

        Greetings from Tiaano. Thank you so much for taking the time to visit ({visitWhen}) our company. Your support and enthusiasm are invaluable.

        Your ongoing support has been instrumental in our success as a company. We look forward to continuing our business relationship all in the future.

        With Warm Regards,
        ABILASH.C
        SENIOR MANAGER - FACILITATION

        TI ANODE FABRICATORS PVT. LTD.
        Tiaano Bhavan, No. 48, Noothanchary, Madambakkam, Chennai-126, INDIA
        Phone: +91 9444569900 Mobile: +91 8939663774
        Email: sales@tiaano.com Website: www.tianode.com
        """;

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
