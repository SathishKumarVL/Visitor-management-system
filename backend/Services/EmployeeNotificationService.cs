using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// What employees are told about their own work: the header bell summary, and reminders to add minutes of meeting
/// (one per visit a few hours after check-out, plus a daily summary of everything still pending).
/// </summary>
public interface IEmployeeNotificationService
{
    Task<NotificationSummaryDto> GetSummaryAsync(ClaimsPrincipal user);

    /// <summary>The user opened the bell: everything listed so far stops counting towards the badge.</summary>
    Task MarkSeenAsync(ClaimsPrincipal user);

    /// <summary>
    /// Emails / pushes the host of each visit that checked out <c>Reminders:MomAfterHours</c> ago without minutes.
    /// Only check-outs inside the look-back window are considered, so an old backlog is left to the daily summary.
    /// </summary>
    Task<int> SendMomRemindersAsync(DateTime utcNow, CancellationToken ct = default);

    /// <summary>Once a day after <c>Reminders:MomDigestTime</c>: one summary per employee of their visits still without minutes.</summary>
    Task<int> SendMomDigestsAsync(DateTime utcNow, CancellationToken ct = default);
}

public sealed class EmployeeNotificationService : IEmployeeNotificationService
{
    private const int ListSize = 10;
    private const int DigestListSize = 25;

    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<EmployeeNotificationService> _logger;

    public EmployeeNotificationService(
        ApplicationDbContext db,
        INotificationService notifications,
        ITenantContext tenant,
        IConfiguration config,
        ILogger<EmployeeNotificationService> logger)
    {
        _db = db;
        _notifications = notifications;
        _tenant = tenant;
        _config = config;
        _logger = logger;
    }

    private bool Enabled => _config.GetValue("Reminders:MomEnabled", true);
    private TimeSpan MomAfter => TimeSpan.FromHours(Math.Clamp(_config.GetValue("Reminders:MomAfterHours", 2.0), 0.25, 72));
    private TimeSpan MomLookback => TimeSpan.FromHours(Math.Clamp(_config.GetValue("Reminders:MomLookbackHours", 24.0), 1, 168));
    private int DigestDays => Math.Clamp(_config.GetValue("Reminders:MomDigestDays", 7), 1, 60);

    private TimeSpan DigestTime =>
        TimeSpan.TryParse(_config["Reminders:MomDigestTime"], out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t
            : new TimeSpan(18, 0, 0);

    public async Task<NotificationSummaryDto> GetSummaryAsync(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin);
        var isDesk = user.IsInRole(AppRoles.Reception) || user.IsInRole(AppRoles.Security);
        var myEmployeeIds = string.IsNullOrEmpty(userId)
            ? new List<Guid>()
            : await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.Id).ToListAsync();

        var today = AppTime.Today;
        var dto = new NotificationSummaryDto { AppointmentsAreMine = !isDesk };
        var seenAt = string.IsNullOrEmpty(userId)
            ? null
            : await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NotificationsSeenAt).FirstOrDefaultAsync();
        // A booking made earlier counts as new on its day until the bell is opened that day.
        var seenToday = seenAt is DateTime s && s >= AppTime.StartOfDayUtc(today);

        // The desk prepares for every visitor expected today; everyone else only for their own.
        var appointments = _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Expected && v.ExpectedDate == today);
        if (!isDesk) appointments = appointments.Where(v => myEmployeeIds.Contains(v.HostEmployeeId));
        dto.AppointmentsTodayCount = await appointments.CountAsync();
        var appointmentsUnseen = seenToday
            ? await appointments.CountAsync(v => v.CreatedAt > seenAt)
            : dto.AppointmentsTodayCount;
        var appointmentRows = await appointments
            .OrderBy(v => v.ExpectedTime)
            .Take(ListSize)
            .Select(v => new
            {
                v.Id,
                v.Visitor.FullName,
                v.Visitor.CompanyName,
                v.ExpectedTime,
                v.CreatedAt,
                HostName = v.HostEmployee.FullName
            })
            .ToListAsync();
        var dateParam = today.ToString("yyyy-MM-dd");
        dto.AppointmentsToday = appointmentRows.Select(v => new NotificationItemDto
        {
            VisitId = v.Id,
            Title = v.FullName,
            Subtitle = JoinParts(v.CompanyName, isDesk ? v.HostName : null),
            Time = v.ExpectedTime?.ToString("HH:mm"),
            Link = $"/visitors/expected?date={dateParam}&visit={v.Id}",
            IsNew = !seenToday || v.CreatedAt > seenAt
        }).ToList();

        var mom = _db.VisitorVisits.AsNoTracking()
            .Where(v => myEmployeeIds.Contains(v.HostEmployeeId)
                && v.Status == VisitStatus.CheckedOut && v.MomEmailSentAt == null && !v.MomNotRequired);
        dto.MomPendingCount = await mom.CountAsync();
        var momUnseen = seenAt is null ? dto.MomPendingCount : await mom.CountAsync(v => v.CheckOutAt > seenAt);
        var momRows = await mom
            .OrderByDescending(v => v.CheckOutAt)
            .Take(ListSize)
            .Select(v => new { v.Id, v.Visitor.FullName, v.Visitor.CompanyName, v.CheckOutAt })
            .ToListAsync();
        dto.MomPending = momRows.Select(v => new NotificationItemDto
        {
            VisitId = v.Id,
            Title = v.FullName,
            Subtitle = v.CompanyName,
            At = AsUtc(v.CheckOutAt),
            Link = $"/visitors/{v.Id}/minutes",
            IsNew = seenAt is null || v.CheckOutAt > seenAt
        }).ToList();

        var approvalsUnseen = 0;
        // Same audience as the Approvals API: admins see all, employees their own.
        if (isAdmin || user.IsInRole(AppRoles.Host))
        {
            var approvals = _db.VisitorVisits.AsNoTracking().Where(v => v.Status == VisitStatus.PendingApproval);
            if (!isAdmin) approvals = approvals.Where(v => myEmployeeIds.Contains(v.HostEmployeeId));
            dto.ApprovalsCount = await approvals.CountAsync();
            approvalsUnseen = seenAt is null ? dto.ApprovalsCount : await approvals.CountAsync(v => v.CreatedAt > seenAt);
            var approvalRows = await approvals
                .OrderByDescending(v => v.CreatedAt)
                .Take(ListSize)
                .Select(v => new { v.Id, v.Visitor.FullName, v.Visitor.CompanyName, HostName = v.HostEmployee.FullName, v.CreatedAt })
                .ToListAsync();
            dto.Approvals = approvalRows.Select(v => new NotificationItemDto
            {
                VisitId = v.Id,
                Title = v.FullName,
                Subtitle = JoinParts(v.CompanyName, isAdmin ? v.HostName : null),
                At = AsUtc(v.CreatedAt),
                Link = $"/visitors/{v.Id}",
                IsNew = seenAt is null || v.CreatedAt > seenAt
            }).ToList();
        }

        dto.Total = dto.AppointmentsTodayCount + dto.MomPendingCount + dto.ApprovalsCount;
        dto.Unseen = appointmentsUnseen + momUnseen + approvalsUnseen;
        return dto;
    }

    public async Task MarkSeenAsync(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return;
        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.NotificationsSeenAt, DateTime.UtcNow));
    }

    public async Task<int> SendMomRemindersAsync(DateTime utcNow, CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var latest = utcNow - MomAfter;
        var earliest = latest - MomLookback;

        // Background work has no ambient tenant: read across tenants, then set the tenant per notification.
        var due = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Status == VisitStatus.CheckedOut && v.MomEmailSentAt == null && !v.MomNotRequired
                && v.CheckOutAt != null && v.CheckOutAt <= latest && v.CheckOutAt > earliest
                && v.HostEmployee.IsActive)
            .OrderBy(v => v.CheckOutAt)
            .Take(500)
            .Select(v => new
            {
                v.Id,
                v.TenantId,
                VisitorName = v.Visitor.FullName,
                Company = v.Visitor.CompanyName,
                v.CheckOutAt,
                HostName = v.HostEmployee.FullName,
                HostEmail = v.HostEmployee.Email,
                HostUserId = v.HostEmployee.UserId,
                HasMinutes = v.MeetingMinutes != null && v.MeetingMinutes != "",
                LocationId = v.VisitLocations.Select(l => (Guid?)l.LocationId).FirstOrDefault()
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var v in due)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(v.HostEmail) && string.IsNullOrWhiteSpace(v.HostUserId)) continue;

            var checkOutLocal = AppTime.ToLocal(AsUtc(v.CheckOutAt)!.Value);
            var link = $"/visitors/{v.Id}/minutes";
            var subject = v.HasMinutes
                ? $"Reminder: send the minutes of meeting for {v.VisitorName}"
                : $"Reminder: add minutes of meeting for {v.VisitorName}";
            var status = v.HasMinutes
                ? "The minutes of meeting are saved but have not been sent yet."
                : "The minutes of meeting have not been added yet.";

            var body = new StringBuilder()
                .Append("Hello ").Append(v.HostName).Append(",\n\n")
                .Append(v.VisitorName);
            if (!string.IsNullOrWhiteSpace(v.Company)) body.Append(" (").Append(v.Company).Append(')');
            body.Append(" met you on ").Append(checkOutLocal.ToString("dd MMM yyyy"))
                .Append(" and checked out at ").Append(checkOutLocal.ToString("HH:mm")).Append(". ")
                .Append(status).Append("\n\n");
            AppendLink(body, "Add them here", link);
            body.Append("If no minutes are needed, open the visit and mark them as not required.\n\n")
                .Append("Tiaano Visitor Management");

            try
            {
                _tenant.Set(v.TenantId);
                if (!string.IsNullOrWhiteSpace(v.HostEmail))
                    await _notifications.NotifyAsync("Email", v.HostEmail.Trim(), subject, body.ToString(),
                        $"mom-reminder:{v.Id}:email", v.LocationId);
                if (!string.IsNullOrWhiteSpace(v.HostUserId))
                    await _notifications.NotifyAsync("Push", v.HostUserId, subject,
                        $"{v.VisitorName} checked out at {checkOutLocal:HH:mm}. {status}",
                        $"mom-reminder:{v.Id}:push", link: link);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Minutes of meeting reminder failed for visit {VisitId}", v.Id);
            }
        }
        return sent;
    }

    public async Task<int> SendMomDigestsAsync(DateTime utcNow, CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        var nowLocal = AppTime.ToLocal(utcNow);
        var digestAt = DigestTime;
        // A short window, so a restart late at night does not send the day's summary hours late.
        if (nowLocal.TimeOfDay < digestAt || nowLocal.TimeOfDay >= digestAt + TimeSpan.FromHours(3)) return 0;

        // Visits still inside the single-reminder delay are left to that reminder.
        var latest = utcNow - MomAfter;
        var earliest = utcNow - TimeSpan.FromDays(DigestDays);
        var pending = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Status == VisitStatus.CheckedOut && v.MomEmailSentAt == null && !v.MomNotRequired
                && v.CheckOutAt != null && v.CheckOutAt <= latest && v.CheckOutAt > earliest
                && v.HostEmployee.IsActive
                && ((v.HostEmployee.Email != null && v.HostEmployee.Email != "") || v.HostEmployee.UserId != null))
            .OrderByDescending(v => v.CheckOutAt)
            .Take(5000)
            .Select(v => new
            {
                v.TenantId,
                v.HostEmployeeId,
                HostName = v.HostEmployee.FullName,
                HostEmail = v.HostEmployee.Email,
                HostUserId = v.HostEmployee.UserId,
                VisitorName = v.Visitor.FullName,
                Company = v.Visitor.CompanyName,
                v.CheckOutAt
            })
            .ToListAsync(ct);

        var day = nowLocal.ToString("yyyyMMdd");
        var sent = 0;
        foreach (var group in pending.GroupBy(p => (p.TenantId, p.HostEmployeeId)))
        {
            ct.ThrowIfCancellationRequested();
            var first = group.First();
            var count = group.Count();
            var subject = count == 1
                ? "1 visit is waiting for minutes of meeting"
                : $"{count} visits are waiting for minutes of meeting";

            var body = new StringBuilder()
                .Append("Hello ").Append(first.HostName).Append(",\n\n")
                .Append(count == 1 ? "This visit has" : "These visits have")
                .Append(" no minutes of meeting yet:\n\n");
            var i = 0;
            foreach (var v in group.Take(DigestListSize))
            {
                var outLocal = AppTime.ToLocal(AsUtc(v.CheckOutAt)!.Value);
                body.Append(++i).Append(". ").Append(v.VisitorName);
                if (!string.IsNullOrWhiteSpace(v.Company)) body.Append(" (").Append(v.Company).Append(')');
                body.Append(" - checked out ").Append(outLocal.ToString("dd MMM, HH:mm")).Append('\n');
            }
            if (count > DigestListSize) body.Append("...and ").Append(count - DigestListSize).Append(" more\n");
            body.Append('\n');
            AppendLink(body, "Open the list", "/visitors/mom-pending");
            body.Append("Tiaano Visitor Management");

            try
            {
                _tenant.Set(first.TenantId);
                var key = $"mom-digest:{first.HostEmployeeId}:{day}";
                if (!string.IsNullOrWhiteSpace(first.HostEmail))
                    await _notifications.NotifyAsync("Email", first.HostEmail.Trim(), subject, body.ToString(), $"{key}:email");
                if (!string.IsNullOrWhiteSpace(first.HostUserId))
                    await _notifications.NotifyAsync("Push", first.HostUserId, subject,
                        "Open the list to add or waive the minutes.", $"{key}:push", link: "/visitors/mom-pending");
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Minutes of meeting summary failed for employee {EmployeeId}", first.HostEmployeeId);
            }
        }
        return sent;
    }

    private void AppendLink(StringBuilder body, string label, string path)
    {
        var baseUrl = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl)) return;
        body.Append(label).Append(": ").Append(baseUrl).Append(path).Append("\n\n");
    }

    private static string? JoinParts(params string?[] parts)
    {
        var joined = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return joined.Length == 0 ? null : joined;
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
}
