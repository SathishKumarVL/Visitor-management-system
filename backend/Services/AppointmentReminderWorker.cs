using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Queues Email + Push reminders for expected visits (appointments) at configured hour offsets.
/// Idempotent via NotificationOutbox.IdempotencyKey.
/// </summary>
public sealed class AppointmentReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AppointmentReminderWorker> _logger;

    public AppointmentReminderWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<AppointmentReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(_config.GetValue("Reminders:PollMinutes", 5), 1, 60);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Appointment reminder sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        var offsets = await ResolveOffsetsAsync(db);
        if (offsets.Count == 0) return;

        var nowLocal = DateTime.Now;
        var window = TimeSpan.FromMinutes(Math.Clamp(_config.GetValue("Reminders:PollMinutes", 5), 1, 60));

        // Background worker has no ambient tenant — read across tenants with filters ignored,
        // then set tenant context before queuing each notification.
        var expected = await db.VisitorVisits.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(v => v.Visitor)
            .Include(v => v.HostEmployee)
            .Where(v => v.Status == VisitStatus.Expected && v.ExpectedDate != null && v.ExpectedTime != null)
            .Take(500)
            .ToListAsync(ct);

        foreach (var visit in expected)
        {
            var expectedAt = visit.ExpectedDate!.Value.ToDateTime(visit.ExpectedTime!.Value);
            foreach (var hours in offsets)
            {
                var fireAt = expectedAt.AddHours(-hours);
                if (nowLocal < fireAt || nowLocal >= fireAt + window)
                    continue;

                tenantCtx.Set(visit.TenantId, visit.SiteId);
                var when = expectedAt.ToString("dd MMM yyyy HH:mm");
                var subject = $"Reminder: {visit.Visitor.FullName} expected in {hours}h";
                var body =
                    $"Appointment reminder ({hours}h before): {visit.Visitor.FullName} from {visit.Visitor.CompanyName} is expected at {when}.";

                var emailKey = $"appt-reminder:{visit.Id}:{hours}h:email";
                var pushKey = $"appt-reminder:{visit.Id}:{hours}h:push";

                try
                {
                    if (!string.IsNullOrWhiteSpace(visit.HostEmployee?.Email))
                        await notifications.NotifyAsync("Email", visit.HostEmployee.Email, subject, body, emailKey);

                    var pushRecipient = visit.HostEmployee?.UserId ?? visit.Visitor.Email;
                    if (!string.IsNullOrWhiteSpace(pushRecipient))
                        await notifications.NotifyAsync("Push", pushRecipient!, subject, body, pushKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to queue reminder for visit {VisitId}", visit.Id);
                }
            }
        }
    }

    private static async Task<List<int>> ResolveOffsetsAsync(ApplicationDbContext db)
    {
        // Prefer first tenant setting; default "24,1".
        var raw = await db.SystemSettings.IgnoreQueryFilters()
            .Where(s => s.Key == "ReminderHoursBefore")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        raw ??= "24,1";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var h) ? h : -1)
            .Where(h => h >= 0)
            .Distinct()
            .ToList();
    }
}
