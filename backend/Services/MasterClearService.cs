using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Empties one master for the whole tenant (go-live clean-up of test data). Everything that depends on the
/// cleared records goes with them: visits and bookings, visitors left with no visits, and logins tied to
/// the cleared employees or locations. The built-in "Others" purpose, Super Admins and the caller are kept.
/// With <c>ids</c> only those records of the master are deleted, with the same dependants.
/// </summary>
public interface IMasterClearService
{
    Task<MasterClearSummaryDto> PreviewAsync(string type, string? callerUserId, IReadOnlyCollection<Guid>? ids = null);
    Task<MasterClearSummaryDto> ClearAsync(string type, string? callerUserId, IReadOnlyCollection<Guid>? ids = null);
}

public sealed class MasterClearService : IMasterClearService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMediaStorageService _media;
    private readonly ILogger<MasterClearService> _logger;

    public MasterClearService(ApplicationDbContext db, ITenantContext tenant, IMediaStorageService media, ILogger<MasterClearService> logger)
    {
        _db = db;
        _tenant = tenant;
        _media = media;
        _logger = logger;
    }

    private sealed class Plan
    {
        public string Type = string.Empty;
        public List<Guid> Locations = [];
        public List<Guid> Departments = [];
        public List<Guid> Employees = [];
        public List<Guid> Designations = [];
        public List<Guid> Purposes = [];
        public List<Guid> Halls = [];
        public List<Guid> FeedbackQuestions = [];
        public List<Guid> PassSeries = [];
        public List<Guid> Visits = [];
        public List<Guid> Visitors = [];
        public List<string> Users = [];
    }

    public async Task<MasterClearSummaryDto> PreviewAsync(string type, string? callerUserId, IReadOnlyCollection<Guid>? ids = null) =>
        Summarize(await BuildPlanAsync(type, callerUserId, ids));

    public async Task<MasterClearSummaryDto> ClearAsync(string type, string? callerUserId, IReadOnlyCollection<Guid>? ids = null)
    {
        var plan = await BuildPlanAsync(type, callerUserId, ids);
        var tenantId = TenantId;
        var files = new List<string>();

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            files.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync();
            await DeleteVisitsAsync(plan, tenantId);
            files.AddRange(await DeleteVisitorsAsync(plan, tenantId));
            await DeleteUsersAsync(plan, tenantId);
            files.AddRange(await DeleteMastersAsync(plan, tenantId));
            await tx.CommitAsync();
        });
        _db.ChangeTracker.Clear();

        foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct())
        {
            try { _media.Delete(file); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete media file {File} after clearing masters.", file); }
        }

        var summary = Summarize(plan);
        _logger.LogWarning("Master '{Type}' cleared for tenant {TenantId} by {User}: {Counts}",
            plan.Type, tenantId, callerUserId,
            string.Join(", ", summary.Items.Select(i => $"{i.Count} {i.Label}")));
        return summary;
    }

    private Guid TenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    private async Task<Plan> BuildPlanAsync(string type, string? callerUserId, IReadOnlyCollection<Guid>? ids)
    {
        if (_tenant.LocationId is not null)
            throw new UnauthorizedAccessException("Only a Super Admin for all locations can clear master data.");
        if (ids is { Count: 0 })
            throw new InvalidOperationException("Select at least one record to delete.");
        if (ids is { Count: > 1000 })
            throw new InvalidOperationException("Select at most 1000 records at a time.");
        var tenantId = TenantId;
        var plan = new Plan { Type = type.Trim().ToLowerInvariant() };

        switch (plan.Type)
        {
            case "locations":
                plan.Locations = await _db.Locations.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            case "departments":
                plan.Departments = await _db.Departments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            case "employees":
                plan.Employees = await _db.Employees.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            case "designations":
                plan.Designations = await _db.Designations.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            case "purposes":
                plan.Purposes = await _db.VisitPurposes.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.Name != VisitPurposeDefaults.OthersName)
                    .Select(x => x.Id).ToListAsync();
                break;
            case "meeting-halls":
                plan.Halls = await _db.MeetingHalls.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            case "feedback-questions":
                plan.FeedbackQuestions = await _db.FeedbackQuestions.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync();
                break;
            default:
                throw new InvalidOperationException("This master cannot be cleared.");
        }

        if (ids is not null)
        {
            var chosen = ids.ToHashSet();
            List<Guid> Keep(List<Guid> all) => all.Where(chosen.Contains).ToList();
            plan.Locations = Keep(plan.Locations);
            plan.Departments = Keep(plan.Departments);
            plan.Employees = Keep(plan.Employees);
            plan.Designations = Keep(plan.Designations);
            plan.Purposes = Keep(plan.Purposes);
            plan.Halls = Keep(plan.Halls);
            plan.FeedbackQuestions = Keep(plan.FeedbackQuestions);
            if (plan.Locations.Count + plan.Departments.Count + plan.Employees.Count + plan.Designations.Count
                + plan.Purposes.Count + plan.Halls.Count + plan.FeedbackQuestions.Count == 0)
                throw new InvalidOperationException("None of the selected records can be deleted.");
        }

        // Records that belong to a cleared location go with it.
        if (plan.Locations.Count > 0)
        {
            var locations = plan.Locations;
            plan.Departments = await _db.Departments.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.LocationId != null && locations.Contains(x.LocationId.Value))
                .Select(x => x.Id).ToListAsync();
            plan.Purposes = await _db.VisitPurposes.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.Name != VisitPurposeDefaults.OthersName
                            && x.LocationId != null && locations.Contains(x.LocationId.Value))
                .Select(x => x.Id).ToListAsync();
            plan.Halls = await _db.MeetingHalls.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && locations.Contains(x.LocationId))
                .Select(x => x.Id).ToListAsync();
            plan.FeedbackQuestions = await _db.FeedbackQuestions.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.LocationId != null && locations.Contains(x.LocationId.Value))
                .Select(x => x.Id).ToListAsync();
            plan.PassSeries = await _db.PassNumberSeries.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.LocationId != null && locations.Contains(x.LocationId.Value))
                .Select(x => x.Id).ToListAsync();
        }

        // Employees cannot outlive their department or location.
        if (plan.Departments.Count > 0 || plan.Locations.Count > 0)
        {
            var departments = plan.Departments;
            var locations = plan.Locations;
            plan.Employees = await _db.Employees.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId
                            && (departments.Contains(x.DepartmentId) || (x.LocationId != null && locations.Contains(x.LocationId.Value))))
                .Select(x => x.Id).ToListAsync();
        }

        {
            var employees = plan.Employees;
            var departments = plan.Departments;
            var halls = plan.Halls;
            var purposes = plan.Purposes;
            var locations = plan.Locations;
            plan.Visits = await _db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId
                            && (employees.Contains(v.HostEmployeeId)
                                || departments.Contains(v.DepartmentId)
                                || (v.MeetingHallId != null && halls.Contains(v.MeetingHallId.Value))
                                || v.VisitPurposes.Any(p => purposes.Contains(p.VisitPurposeId))
                                || v.VisitLocations.Any(l => locations.Contains(l.LocationId))))
                .Select(v => v.Id).ToListAsync();
        }

        if (plan.Visits.Count > 0)
        {
            var visits = plan.Visits;
            var touched = await _db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => visits.Contains(v.Id)).Select(v => v.VisitorId).Distinct().ToListAsync();
            var stillVisiting = await _db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => touched.Contains(v.VisitorId) && !visits.Contains(v.Id))
                .Select(v => v.VisitorId).Distinct().ToListAsync();
            plan.Visitors = touched.Except(stillVisiting).ToList();
        }

        plan.Users = await UsersToDeleteAsync(plan, tenantId, callerUserId);
        return plan;
    }

    /// <summary>
    /// Logins bound to a cleared location (they would otherwise silently become all-location logins), and
    /// host-only logins of cleared employees. Super Admins and the caller are never deleted.
    /// </summary>
    private async Task<List<string>> UsersToDeleteAsync(Plan plan, Guid tenantId, string? callerUserId)
    {
        var candidates = new HashSet<string>();
        if (plan.Locations.Count > 0)
        {
            var locations = plan.Locations;
            candidates.UnionWith(await _db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && u.LocationId != null && locations.Contains(u.LocationId.Value))
                .Select(u => u.Id).ToListAsync());
        }
        if (plan.Employees.Count > 0)
        {
            var employees = plan.Employees;
            var linked = await _db.Employees.IgnoreQueryFilters()
                .Where(e => employees.Contains(e.Id) && e.UserId != null)
                .Select(e => e.UserId!).Distinct().ToListAsync();
            var withOtherRoles = await (from ur in _db.UserRoles
                                        join r in _db.Roles on ur.RoleId equals r.Id
                                        where linked.Contains(ur.UserId) && r.Name != AppRoles.Host
                                        select ur.UserId).Distinct().ToListAsync();
            candidates.UnionWith(linked.Except(withOtherRoles));
        }
        if (candidates.Count == 0) return [];

        var ids = candidates.ToList();
        var superAdmins = await (from ur in _db.UserRoles
                                 join r in _db.Roles on ur.RoleId equals r.Id
                                 where ids.Contains(ur.UserId) && r.Name == AppRoles.SuperAdmin
                                 select ur.UserId).Distinct().ToListAsync();
        return await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id) && !superAdmins.Contains(u.Id) && u.Id != callerUserId)
            .Select(u => u.Id).ToListAsync();
    }

    private async Task DeleteVisitsAsync(Plan plan, Guid tenantId)
    {
        var employees = plan.Employees;
        if (employees.Count > 0)
        {
            await _db.Approvals.Where(a => a.HostEmployeeId != null && employees.Contains(a.HostEmployeeId.Value)).ExecuteDeleteAsync();
            await _db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId && v.GroupEmployeeId != null && employees.Contains(v.GroupEmployeeId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.GroupEmployeeId, (Guid?)null));
        }
        if (plan.Visits.Count == 0) return;

        var visits = plan.Visits;
        await _db.VisitFeedbacks.IgnoreQueryFilters().Where(f => visits.Contains(f.VisitorVisitId)).ExecuteDeleteAsync();
        await _db.VisitorDocuments.Where(d => d.VisitorVisitId != null && visits.Contains(d.VisitorVisitId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.VisitorVisitId, (Guid?)null));

        // Bell entries and pushes link to the visit page; drop the ones that would open a deleted visit.
        var linked = await _db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Link != null)
            .Select(n => new { n.Id, n.Link })
            .ToListAsync();
        var visitKeys = visits.Select(v => v.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deadLinks = linked
            .Where(n => n.Link!.Split('/', '?', '#', '&', '=').Any(visitKeys.Contains))
            .Select(n => n.Id)
            .ToList();
        if (deadLinks.Count > 0)
            await _db.NotificationOutbox.IgnoreQueryFilters().Where(n => deadLinks.Contains(n.Id)).ExecuteDeleteAsync();

        // Approvals, passes, roll-call entries and the visit's purpose/location rows cascade in the database.
        await _db.VisitorVisits.IgnoreQueryFilters().Where(v => visits.Contains(v.Id)).ExecuteDeleteAsync();
    }

    private async Task<List<string>> DeleteVisitorsAsync(Plan plan, Guid tenantId)
    {
        if (plan.Visitors.Count == 0) return [];
        var visitors = plan.Visitors;
        var photos = await _db.VisitorPhotos.Where(p => visitors.Contains(p.VisitorId)).Select(p => p.FilePath).ToListAsync();
        await _db.VisitFeedbacks.IgnoreQueryFilters().Where(f => visitors.Contains(f.VisitorId)).ExecuteDeleteAsync();
        // Photos, ID documents and face templates cascade in the database.
        await _db.Visitors.IgnoreQueryFilters().Where(v => v.TenantId == tenantId && visitors.Contains(v.Id)).ExecuteDeleteAsync();
        return photos;
    }

    private async Task DeleteUsersAsync(Plan plan, Guid tenantId)
    {
        if (plan.Users.Count == 0) return;
        var users = plan.Users;
        var visits = _db.VisitorVisits.IgnoreQueryFilters().Where(v => v.TenantId == tenantId);
        await visits.Where(v => v.CheckedInByUserId != null && users.Contains(v.CheckedInByUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.CheckedInByUserId, (string?)null));
        await visits.Where(v => v.SecurityCheckInUserId != null && users.Contains(v.SecurityCheckInUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.SecurityCheckInUserId, (string?)null));
        await visits.Where(v => v.CheckedOutByUserId != null && users.Contains(v.CheckedOutByUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.CheckedOutByUserId, (string?)null));
        await visits.Where(v => v.GroupEmployeeUserId != null && users.Contains(v.GroupEmployeeUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.GroupEmployeeUserId, (string?)null));
        await _db.Approvals.Where(a => a.RequestedToUserId != null && users.Contains(a.RequestedToUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RequestedToUserId, (string?)null));
        await _db.Approvals.Where(a => a.ActionByUserId != null && users.Contains(a.ActionByUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActionByUserId, (string?)null));
        await _db.VisitorPasses.Where(p => p.IssuedByUserId != null && users.Contains(p.IssuedByUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IssuedByUserId, (string?)null));
        await _db.EmergencyRollCallEvents.IgnoreQueryFilters()
            .Where(e => e.RecordedByUserId != null && users.Contains(e.RecordedByUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RecordedByUserId, (string?)null));
        // Roles, refresh tokens, face logins, push devices and pass allocations cascade; employees are unlinked.
        await _db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId && users.Contains(u.Id)).ExecuteDeleteAsync();
    }

    private async Task<List<string>> DeleteMastersAsync(Plan plan, Guid tenantId)
    {
        var files = new List<string>();

        if (plan.FeedbackQuestions.Count > 0)
        {
            var questions = plan.FeedbackQuestions;
            await _db.VisitFeedbackAnswers.Where(a => a.FeedbackQuestionId != null && questions.Contains(a.FeedbackQuestionId.Value)).ExecuteDeleteAsync();
            await _db.FeedbackQuestions.IgnoreQueryFilters().Where(q => questions.Contains(q.Id)).ExecuteDeleteAsync();
        }
        if (plan.Halls.Count > 0)
        {
            var halls = plan.Halls;
            await _db.MeetingHalls.IgnoreQueryFilters().Where(h => halls.Contains(h.Id)).ExecuteDeleteAsync();
        }
        if (plan.Employees.Count > 0)
        {
            var employees = plan.Employees;
            files.AddRange(await _db.Employees.IgnoreQueryFilters()
                .Where(e => employees.Contains(e.Id) && e.PhotoPath != null).Select(e => e.PhotoPath!).ToListAsync());
            await _db.Employees.IgnoreQueryFilters().Where(e => employees.Contains(e.Id)).ExecuteDeleteAsync();
        }
        if (plan.Departments.Count > 0)
        {
            var departments = plan.Departments;
            await _db.Users.IgnoreQueryFilters().Where(u => u.DepartmentId != null && departments.Contains(u.DepartmentId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DepartmentId, (Guid?)null));
            await _db.Departments.IgnoreQueryFilters().Where(d => departments.Contains(d.Id)).ExecuteDeleteAsync();
        }
        if (plan.Purposes.Count > 0)
        {
            var purposes = plan.Purposes;
            await _db.VisitPurposes.IgnoreQueryFilters().Where(p => purposes.Contains(p.Id)).ExecuteDeleteAsync();
        }
        if (plan.Designations.Count > 0)
        {
            var designations = plan.Designations;
            await _db.Designations.IgnoreQueryFilters().Where(d => designations.Contains(d.Id)).ExecuteDeleteAsync();
        }
        if (plan.Locations.Count > 0)
        {
            var locations = plan.Locations;
            var series = plan.PassSeries;
            await _db.PassNumberAllocations.IgnoreQueryFilters().Where(a => series.Contains(a.SeriesId)).ExecuteDeleteAsync();
            await _db.PassNumberSeries.IgnoreQueryFilters().Where(s => series.Contains(s.Id)).ExecuteDeleteAsync();
            await _db.MailContentTemplates.IgnoreQueryFilters()
                .Where(m => m.LocationId != null && locations.Contains(m.LocationId.Value)).ExecuteDeleteAsync();
            var prefixes = locations.SelectMany(id => new[] { $"SmtpLoc:{id:N}:", $"BrandLoc:{id:N}:" }).ToList();
            var settingIds = (await _db.SystemSettings.IgnoreQueryFilters()
                    .Where(s => s.TenantId == tenantId && (s.Key.StartsWith("SmtpLoc:") || s.Key.StartsWith("BrandLoc:")))
                    .Select(s => new { s.Id, s.Key }).ToListAsync())
                .Where(s => prefixes.Any(p => s.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .Select(s => s.Id).ToList();
            if (settingIds.Count > 0)
                await _db.SystemSettings.IgnoreQueryFilters().Where(s => settingIds.Contains(s.Id)).ExecuteDeleteAsync();
            await _db.Users.IgnoreQueryFilters().Where(u => u.LocationId != null && locations.Contains(u.LocationId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LocationId, (Guid?)null));
            await _db.NotificationOutbox.IgnoreQueryFilters().Where(n => n.LocationId != null && locations.Contains(n.LocationId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.LocationId, (Guid?)null));
            await _db.Locations.IgnoreQueryFilters().Where(l => locations.Contains(l.Id)).ExecuteDeleteAsync();
        }
        return files;
    }

    private static MasterClearSummaryDto Summarize(Plan plan)
    {
        var items = new List<MasterClearCountDto>
        {
            new("locations", plan.Locations.Count),
            new("departments", plan.Departments.Count),
            new("employees", plan.Employees.Count),
            new("designations", plan.Designations.Count),
            new("purposes", plan.Purposes.Count),
            new("meeting halls", plan.Halls.Count),
            new("feedback questions", plan.FeedbackQuestions.Count),
            new("pass number series", plan.PassSeries.Count),
            new("visits and bookings", plan.Visits.Count),
            new("visitors (with their photos and face data)", plan.Visitors.Count),
            new("logins", plan.Users.Count),
        };
        return new MasterClearSummaryDto
        {
            Type = plan.Type,
            Items = items.Where(i => i.Count > 0).ToList(),
        };
    }
}
