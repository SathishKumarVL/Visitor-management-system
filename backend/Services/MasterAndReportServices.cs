using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IMasterDataService
{
    Task<IReadOnlyList<MasterItemDto>> GetDepartmentsAsync(bool activeOnly = true, Guid? locationId = null);
    Task<MasterItemDto> UpsertDepartmentAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<EmployeeDto>> GetHostsAsync(Guid? departmentId = null, Guid? locationId = null, bool activeOnly = true);
    Task<EmployeeDto> UpsertEmployeeAsync(Guid? id, CreateEmployeeRequest request, string? user);
    Task DeactivateEmployeeAsync(Guid id, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetPurposesAsync(bool activeOnly = true, Guid? locationId = null);
    Task<MasterItemDto> UpsertPurposeAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetLocationsAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertLocationAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetIdTypesAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertIdTypeAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task DeactivateMasterAsync(string type, Guid id, string? user);
    Task<IReadOnlyList<MailContentTemplateDto>> GetMailContentsAsync(Guid? locationId = null);
    Task<MailContentTemplateDto> UpsertMailContentAsync(MailContentUpsertRequest request, string? user);
    Task<IReadOnlyList<MeetingHallDto>> GetMeetingHallsAsync(bool activeOnly = true, Guid? locationId = null);
    Task<MeetingHallDto> UpsertMeetingHallAsync(Guid? id, MeetingHallUpsertRequest request, string? user);
}

public class MasterDataService : IMasterDataService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEntitlementService _entitlements;

    public MasterDataService(ApplicationDbContext db, ITenantContext tenant, IEntitlementService entitlements)
    {
        _db = db;
        _tenant = tenant;
        _entitlements = entitlements;
    }

    private Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    /// <summary>
    /// A user bound to a location only works with that location's employees and departments (plus shared
    /// ones with no location). Returns false when they asked for some other location.
    /// </summary>
    private bool TryScopeLocation(ref Guid? locationId)
    {
        if (_tenant.LocationId is not Guid scope) return true;
        if (locationId.HasValue && locationId.Value != scope) return false;
        locationId = scope;
        return true;
    }

    private void EnsureInUserLocation(Guid? recordLocationId, string what)
    {
        if (_tenant.LocationId is Guid scope && recordLocationId.HasValue && recordLocationId.Value != scope)
            throw new UnauthorizedAccessException($"This {what} belongs to another location.");
    }

    public async Task<IReadOnlyList<MasterItemDto>> GetDepartmentsAsync(bool activeOnly = true, Guid? locationId = null)
    {
        if (!TryScopeLocation(ref locationId)) return [];
        var q = _db.Departments.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        if (locationId.HasValue)
            q = q.Where(x => x.LocationId == null || x.LocationId == locationId.Value);
        return await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new MasterItemDto
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code,
                Intercom = x.Intercom,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder,
                LocationId = x.LocationId,
                LocationName = x.Location != null ? x.Location.Name : null,
            })
            .ToListAsync();
    }

    public async Task<MasterItemDto> UpsertDepartmentAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        Department entity;
        if (id.HasValue)
        {
            entity = await _db.Departments.FindAsync(id.Value) ?? throw new InvalidOperationException("Department not found.");
            EnsureInUserLocation(entity.LocationId, "department");
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new Department { TenantId = CurrentTenantId, CreatedBy = user };
            _db.Departments.Add(entity);
        }
        EnsureInUserLocation(request.LocationId, "department");
        entity.Name = request.Name.Trim();
        entity.Code = request.Code;
        entity.Intercom = request.Intercom;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.LocationId = request.LocationId ?? (id.HasValue ? null : _tenant.LocationId);
        if (entity.LocationId.HasValue)
        {
            var locOk = await _db.Locations.AnyAsync(l => l.Id == entity.LocationId.Value);
            if (!locOk) throw new InvalidOperationException("Selected location was not found.");
        }
        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(e => e.Location).LoadAsync();
        return new MasterItemDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            Intercom = entity.Intercom,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            LocationId = entity.LocationId,
            LocationName = entity.Location?.Name,
        };
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetHostsAsync(Guid? departmentId = null, Guid? locationId = null, bool activeOnly = true)
    {
        if (!TryScopeLocation(ref locationId)) return [];
        var q = _db.Employees.Include(e => e.Department).Include(e => e.Location).AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(e => e.IsActive);
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        if (locationId.HasValue)
            q = q.Where(e => e.LocationId == null || e.LocationId == locationId.Value);
        return await q.OrderBy(e => e.FullName).Select(e => new EmployeeDto
        {
            Id = e.Id,
            FullName = e.FullName,
            Code = e.Code,
            Email = e.Email,
            Phone = e.Phone,
            Intercom = e.Intercom,
            Designation = e.Designation,
            DepartmentId = e.DepartmentId,
            DepartmentName = e.Department.Name,
            LocationId = e.LocationId,
            LocationName = e.Location != null ? e.Location.Name : null,
            UserId = e.UserId,
            UserName = _db.Users.Where(u => u.Id == e.UserId).Select(u => u.UserName).FirstOrDefault(),
            IsActive = e.IsActive,
            DateOfBirth = e.DateOfBirth,
            Gender = e.Gender,
            HasFace = e.FaceDescriptor != null
        }).ToListAsync();
    }

    public async Task<EmployeeDto> UpsertEmployeeAsync(Guid? id, CreateEmployeeRequest request, string? user)
    {
        Employee entity;
        if (id.HasValue)
        {
            entity = await _db.Employees.Include(e => e.Department).Include(e => e.Location).FirstOrDefaultAsync(e => e.Id == id)
                ?? throw new InvalidOperationException("Employee not found.");
            EnsureInUserLocation(entity.LocationId, "employee");
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new Employee { TenantId = CurrentTenantId, CreatedBy = user };
            _db.Employees.Add(entity);
        }
        entity.FullName = request.FullName.Trim();
        entity.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        entity.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (string.IsNullOrWhiteSpace(request.Phone))
            entity.Phone = null;
        else
        {
            var digits = System.Text.RegularExpressions.Regex.Replace(request.Phone.Trim(), @"\D", "");
            if (digits.Length == 12 && digits.StartsWith("91"))
                digits = digits[2..];
            if (digits.Length == 11 && digits.StartsWith("0"))
                digits = digits[1..];
            if (digits.Length != 10)
                throw new InvalidOperationException("Phone number must be exactly 10 digits.");
            entity.Phone = digits;
        }
        entity.Intercom = string.IsNullOrWhiteSpace(request.Intercom) ? null : request.Intercom.Trim();
        entity.Designation = string.IsNullOrWhiteSpace(request.Designation) ? null : request.Designation.Trim();
        EnsureInUserLocation(request.LocationId, "employee");
        entity.DepartmentId = request.DepartmentId;
        entity.LocationId = request.LocationId ?? (id.HasValue ? null : _tenant.LocationId);
        entity.UserId = request.UserId;
        entity.IsActive = request.IsActive;

        var deptOk = await _db.Departments.AnyAsync(d => d.Id == entity.DepartmentId);
        if (!deptOk) throw new InvalidOperationException("Selected department was not found.");
        if (entity.LocationId.HasValue)
        {
            var locOk = await _db.Locations.AnyAsync(l => l.Id == entity.LocationId.Value);
            if (!locOk) throw new InvalidOperationException("Selected location was not found.");
        }

        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(e => e.Department).LoadAsync();
        await _db.Entry(entity).Reference(e => e.Location).LoadAsync();
        return new EmployeeDto
        {
            Id = entity.Id,
            FullName = entity.FullName,
            Code = entity.Code,
            Email = entity.Email,
            Phone = entity.Phone,
            Intercom = entity.Intercom,
            Designation = entity.Designation,
            DepartmentId = entity.DepartmentId,
            DepartmentName = entity.Department.Name,
            LocationId = entity.LocationId,
            LocationName = entity.Location?.Name,
            UserId = entity.UserId,
            UserName = entity.UserId == null
                ? null
                : await _db.Users.Where(u => u.Id == entity.UserId).Select(u => u.UserName).FirstOrDefaultAsync(),
            IsActive = entity.IsActive
        };
    }

    public async Task DeactivateEmployeeAsync(Guid id, string? user)
    {
        var entity = await _db.Employees.FindAsync(id) ?? throw new InvalidOperationException("Employee not found.");
        EnsureInUserLocation(entity.LocationId, "employee");
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<MasterItemDto>> GetPurposesAsync(bool activeOnly = true, Guid? locationId = null)
    {
        await VisitPurposeDefaults.EnsureOthersPurposeAsync(_db, CurrentTenantId);
        if (!TryScopeLocation(ref locationId)) return [];
        var q = _db.VisitPurposes.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        if (locationId.HasValue)
            q = q.Where(x => x.LocationId == null || x.LocationId == locationId.Value);
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Code = x.Code,
            IsActive = x.IsActive,
            SortOrder = x.SortOrder,
            LocationId = x.LocationId,
            LocationName = x.Location != null ? x.Location.Name : null,
        }).ToListAsync();
    }

    public async Task<MasterItemDto> UpsertPurposeAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        VisitPurpose entity;
        if (id.HasValue)
        {
            entity = await _db.VisitPurposes.FindAsync(id) ?? throw new InvalidOperationException("Purpose not found.");
            EnsureInUserLocation(entity.LocationId, "purpose");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new VisitPurpose { TenantId = CurrentTenantId, CreatedBy = user }; _db.VisitPurposes.Add(entity); }
        EnsureInUserLocation(request.LocationId, "purpose");
        entity.Name = request.Name.Trim();
        entity.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.LocationId = request.LocationId
            ?? (string.Equals(entity.Name, "Others", StringComparison.OrdinalIgnoreCase) ? null : _tenant.LocationId);
        if (!entity.LocationId.HasValue
            && !string.Equals(entity.Name, "Others", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Purpose must be assigned to a location.");
        if (entity.LocationId.HasValue)
        {
            var locOk = await _db.Locations.AnyAsync(l => l.Id == entity.LocationId.Value);
            if (!locOk) throw new InvalidOperationException("Selected location was not found.");
        }
        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(e => e.Location).LoadAsync();
        return new MasterItemDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            LocationId = entity.LocationId,
            LocationName = entity.Location?.Name,
        };
    }

    /// <summary>A location-bound user only reads and edits their own location's copy, never the tenant default.</summary>
    private Guid? ScopeMailLocation(Guid? locationId)
    {
        if (_tenant.LocationId is not Guid scope) return locationId;
        if (locationId.HasValue && locationId.Value != scope)
            throw new UnauthorizedAccessException("This mail content belongs to another location.");
        return scope;
    }

    public async Task<IReadOnlyList<MailContentTemplateDto>> GetMailContentsAsync(Guid? locationId = null)
    {
        locationId = ScopeMailLocation(locationId);
        await EnsureDefaultMailContentsAsync(null);
        if (locationId is Guid)
            await EnsureDefaultMailContentsAsync(locationId);

        var q = _db.MailContentTemplates.AsNoTracking().AsQueryable();
        q = locationId is Guid loc
            ? q.Where(x => x.LocationId == loc)
            : q.Where(x => x.LocationId == null);

        return await q
            .OrderBy(x => x.Kind)
            .Select(x => new MailContentTemplateDto
            {
                Id = x.Id,
                Kind = x.Kind,
                KindLabel = x.Kind.ToString(),
                Subject = x.Subject,
                Body = x.Body,
                IsActive = x.IsActive,
                LocationId = x.LocationId,
                LocationName = x.Location != null ? x.Location.Name : null,
            }).ToListAsync();
    }

    public async Task<MailContentTemplateDto> UpsertMailContentAsync(MailContentUpsertRequest request, string? user)
    {
        request.LocationId = ScopeMailLocation(request.LocationId);
        if (request.LocationId is Guid locId)
        {
            var locOk = await _db.Locations.AnyAsync(l => l.Id == locId);
            if (!locOk)
                throw new InvalidOperationException("Location not found.");
        }

        var entity = await _db.MailContentTemplates.FirstOrDefaultAsync(x =>
            x.Kind == request.Kind
            && ((request.LocationId == null && x.LocationId == null)
                || (request.LocationId != null && x.LocationId == request.LocationId)));
        if (entity is null)
        {
            entity = new MailContentTemplate
            {
                TenantId = CurrentTenantId,
                Kind = request.Kind,
                LocationId = request.LocationId,
                CreatedBy = user,
            };
            _db.MailContentTemplates.Add(entity);
        }
        else
        {
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }

        entity.Subject = request.Subject.Trim();
        entity.Body = request.Body.Trim();
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();

        string? locationName = null;
        if (entity.LocationId is Guid lid)
            locationName = await _db.Locations.AsNoTracking()
                .Where(l => l.Id == lid)
                .Select(l => l.Name)
                .FirstOrDefaultAsync();

        return new MailContentTemplateDto
        {
            Id = entity.Id,
            Kind = entity.Kind,
            KindLabel = entity.Kind.ToString(),
            Subject = entity.Subject,
            Body = entity.Body,
            IsActive = entity.IsActive,
            LocationId = entity.LocationId,
            LocationName = locationName,
        };
    }

    private async Task EnsureDefaultMailContentsAsync(Guid? locationId = null)
    {
        foreach (var kind in Enum.GetValues<MailContentKind>())
        {
            var exists = locationId is Guid loc
                ? await _db.MailContentTemplates.AnyAsync(x => x.Kind == kind && x.LocationId == loc)
                : await _db.MailContentTemplates.AnyAsync(x => x.Kind == kind && x.LocationId == null);
            if (exists) continue;

            string subject;
            string body;
            if (locationId is Guid)
            {
                var fallback = await _db.MailContentTemplates.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Kind == kind && x.LocationId == null);
                if (fallback is not null)
                {
                    subject = fallback.Subject;
                    body = fallback.Body;
                }
                else
                {
                    (subject, body) = DefaultMailContent(kind);
                }
            }
            else
            {
                (subject, body) = DefaultMailContent(kind);
            }

            _db.MailContentTemplates.Add(new MailContentTemplate
            {
                TenantId = CurrentTenantId,
                Kind = kind,
                LocationId = locationId,
                Subject = subject,
                Body = body,
                IsActive = true,
                CreatedBy = "system",
            });
        }
        await _db.SaveChangesAsync();

        // Keep existing templates in step with the placeholders (tenant defaults + scoped).
        IQueryable<MailContentTemplate> scope = _db.MailContentTemplates;
        scope = locationId is Guid loc2
            ? scope.Where(x => x.LocationId == loc2)
            : scope.Where(x => x.LocationId == null);

        var checkout = await scope.FirstOrDefaultAsync(x => x.Kind == MailContentKind.CheckOut);
        if (checkout is not null)
        {
            var changed = false;
            var withoutMinutes = VisitMailService.RemoveMeetingMinutesSection(checkout.Body);
            if (withoutMinutes != checkout.Body)
            {
                checkout.Body = withoutMinutes;
                changed = true;
            }
            if (checkout.Body.Contains("Dear {{VisitorName}}", StringComparison.Ordinal)
                && !checkout.Body.Contains("{{Salutation}}", StringComparison.Ordinal))
            {
                checkout.Body = checkout.Body.Replace("Dear {{VisitorName}}", "Dear {{Salutation}} {{VisitorName}}", StringComparison.Ordinal);
                changed = true;
            }
            if (changed)
            {
                checkout.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        foreach (var kind in new[] { MailContentKind.CheckIn, MailContentKind.Appointment })
        {
            var tpl = await scope.FirstOrDefaultAsync(x => x.Kind == kind);
            if (tpl is null || tpl.Body.Contains("{{Salutation}}", StringComparison.Ordinal)) continue;
            if (tpl.Body.Contains("{{VisitorName}}", StringComparison.Ordinal))
            {
                tpl.Body = tpl.Body.Replace("{{VisitorName}}", "{{Salutation}} {{VisitorName}}", StringComparison.Ordinal);
                tpl.Subject = tpl.Subject.Replace("{{VisitorName}}", "{{Salutation}} {{VisitorName}}", StringComparison.Ordinal);
                tpl.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }

    internal static (string Subject, string Body) DefaultMailContent(MailContentKind kind) => kind switch
    {
        MailContentKind.CheckIn => (
            "Visitor checked in: {{Salutation}} {{VisitorName}}",
            """
            Dear {{VisitingPerson}},

            {{Salutation}} {{VisitorName}} from {{Company}} has checked in at {{Location}}.

            Visit number: {{VisitorNumber}}
            Department: {{Department}}
            Purpose: {{Purpose}}
            Date/Time: {{Date}} {{Time}}

            Regards,
            Tiaano VMS
            """),
        MailContentKind.CheckOut => (
            "Thank you for visiting {{Location}}",
            """
            Dear {{Salutation}} {{VisitorName}},

            Thank you for visiting {{Location}}. Please visit again!

            Visiting person: {{VisitingPerson}}
            Visit number: {{VisitorNumber}}
            Department: {{Department}}
            Purpose: {{Purpose}}
            Check-out: {{Date}} {{Time}}

            Regards,
            Tiaano VMS
            """),
        MailContentKind.MeetingMinutes => (
            "Minutes of meeting: {{VisitingPerson}} and {{Salutation}} {{VisitorName}}, {{Date}}",
            """
            Dear {{Salutation}} {{VisitorName}},

            Thank you for meeting {{VisitingPerson}} at {{Location}}. Please find the minutes of the meeting below.

            Visit number: {{VisitorNumber}}
            Meeting date: {{Date}}
            Department: {{Department}}
            Purpose: {{Purpose}}
            Meeting hall: {{MeetingHall}}

            Discussion points:
            {{Discussion}}

            Decisions:
            {{Decisions}}

            Action items:
            {{ActionItems}}

            Next meeting: {{NextMeetingDate}}

            Regards,
            {{VisitingPerson}}
            """),
        MailContentKind.Appointment => (
            "Appointment scheduled: {{Salutation}} {{VisitorName}}",
            """
            Dear {{VisitingPerson}},

            An appointment has been scheduled for {{Salutation}} {{VisitorName}} ({{Company}}) at {{Location}}.

            Visit number: {{VisitorNumber}}
            Department: {{Department}}
            Purpose: {{Purpose}}
            Date/Time: {{Date}} {{Time}}

            Regards,
            Tiaano VMS
            """),
        MailContentKind.AppointmentVisitor => (
            "Your appointment at {{Location}} is confirmed",
            """
            Dear {{Salutation}} {{VisitorName}},

            Your appointment with {{VisitingPerson}} at {{Location}} has been scheduled.

            Date/Time: {{Date}} {{Time}}
            Visiting person: {{VisitingPerson}}
            Department: {{Department}}
            Purpose: {{Purpose}}
            Visit number: {{VisitorNumber}}

            Please show this email at the reception when you arrive.

            Regards,
            Tiaano VMS
            """),
        _ => ("Notification", "Visitor update for {{Salutation}} {{VisitorName}}."),
    };

    public Task<IReadOnlyList<MasterItemDto>> GetLocationsAsync(bool activeOnly = true) =>
        GetLocationsInternal(activeOnly);

    private async Task<IReadOnlyList<MasterItemDto>> GetLocationsInternal(bool activeOnly)
    {
        var q = _db.Locations.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        if (_tenant.LocationId is Guid scope) q = q.Where(x => x.Id == scope);
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Code = x.Code,
            IsActive = x.IsActive,
            SortOrder = x.SortOrder,
            RequiresPlantNumber = x.RequiresPlantNumber,
            RequiresOtherText = x.RequiresOtherText,
        }).ToListAsync();
    }

    public async Task<MasterItemDto> UpsertLocationAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        if (id is null)
        {
            if (_tenant.LocationId is not null)
                throw new UnauthorizedAccessException("Only a Super Admin can add locations.");
            await _entitlements.EnsureCanCreateLocationAsync();
        }

        Location entity;
        if (id.HasValue)
        {
            entity = await _db.Locations.FindAsync(id) ?? throw new InvalidOperationException("Location not found.");
            EnsureInUserLocation(entity.Id, "location");
            if (_tenant.LocationId is not null && !request.IsActive)
                throw new UnauthorizedAccessException("Only a Super Admin can deactivate a location.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new Location { TenantId = CurrentTenantId, CreatedBy = user }; _db.Locations.Add(entity); }
        entity.Name = request.Name.Trim();
        entity.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.RequiresPlantNumber = request.RequiresPlantNumber;
        entity.RequiresOtherText = request.RequiresOtherText;
        await _db.SaveChangesAsync();

        return new MasterItemDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            RequiresPlantNumber = entity.RequiresPlantNumber,
            RequiresOtherText = entity.RequiresOtherText,
        };
    }

    public async Task<IReadOnlyList<MeetingHallDto>> GetMeetingHallsAsync(bool activeOnly = true, Guid? locationId = null)
    {
        if (!TryScopeLocation(ref locationId)) return [];
        var q = _db.MeetingHalls.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(h => h.IsActive);
        if (locationId.HasValue) q = q.Where(h => h.LocationId == locationId.Value);
        var halls = await q
            .OrderBy(h => h.Location.SortOrder).ThenBy(h => h.Location.Name)
            .ThenBy(h => h.SeatingCapacity).ThenBy(h => h.Name)
            .Select(h => new MeetingHallDto
            {
                Id = h.Id,
                Code = h.Code,
                Name = h.Name,
                SeatingCapacity = h.SeatingCapacity,
                IntercomNo = h.IntercomNo,
                LocationId = h.LocationId,
                LocationName = h.Location.Name,
                IsActive = h.IsActive,
            })
            .ToListAsync();
        if (halls.Count == 0) return halls;

        // Visits are location-filtered for bound users; occupancy must reflect every visit in the tenant.
        var tenantId = CurrentTenantId;
        var hallIds = halls.Select(h => h.Id).ToList();
        var occupied = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.Status == VisitStatus.Inside
                        && v.MeetingHallId != null && hallIds.Contains(v.MeetingHallId.Value))
            .Select(v => new { HallId = v.MeetingHallId!.Value, v.VisitNumber, VisitorName = v.Visitor.FullName })
            .ToListAsync();
        foreach (var hall in halls)
        {
            var visit = occupied.FirstOrDefault(o => o.HallId == hall.Id);
            hall.OccupiedByVisitNumber = visit?.VisitNumber;
            hall.OccupiedByVisitorName = visit?.VisitorName;
        }
        return halls;
    }

    public async Task<MeetingHallDto> UpsertMeetingHallAsync(Guid? id, MeetingHallUpsertRequest request, string? user)
    {
        MeetingHall entity;
        if (id.HasValue)
        {
            entity = await _db.MeetingHalls.FindAsync(id.Value) ?? throw new InvalidOperationException("Meeting hall not found.");
            EnsureInUserLocation(entity.LocationId, "meeting hall");
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new MeetingHall { TenantId = CurrentTenantId, CreatedBy = user };
        }

        var code = (request.Code ?? string.Empty).Trim();
        var name = (request.Name ?? string.Empty).Trim();
        if (code.Length == 0) throw new InvalidOperationException("Code is required.");
        if (name.Length == 0) throw new InvalidOperationException("Name is required.");
        if (request.SeatingCapacity < 1) throw new InvalidOperationException("Seating capacity must be at least 1.");

        var locationId = request.LocationId ?? _tenant.LocationId
            ?? throw new InvalidOperationException("Select a location for this meeting hall.");
        EnsureInUserLocation(locationId, "meeting hall");
        if (!await _db.Locations.AnyAsync(l => l.Id == locationId))
            throw new InvalidOperationException("Selected location was not found.");
        if (await _db.MeetingHalls.AnyAsync(h => h.Code == code && h.Id != entity.Id))
            throw new InvalidOperationException($"Meeting hall code '{code}' is already in use.");

        entity.Code = code;
        entity.Name = name;
        entity.SeatingCapacity = request.SeatingCapacity;
        entity.IntercomNo = string.IsNullOrWhiteSpace(request.IntercomNo) ? null : request.IntercomNo.Trim();
        entity.LocationId = locationId;
        entity.IsActive = request.IsActive;
        if (!id.HasValue) _db.MeetingHalls.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetMeetingHallsAsync(activeOnly: false, locationId: entity.LocationId)).First(h => h.Id == entity.Id);
    }

    public Task<IReadOnlyList<MasterItemDto>> GetIdTypesAsync(bool activeOnly = true) =>
        GetSimpleAsync(_db.IdTypes, activeOnly);

    public async Task<MasterItemDto> UpsertIdTypeAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        IdType entity;
        if (id.HasValue)
        {
            entity = await _db.IdTypes.FindAsync(id) ?? throw new InvalidOperationException("ID type not found.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new IdType { TenantId = CurrentTenantId, CreatedBy = user }; _db.IdTypes.Add(entity); }
        entity.Name = request.Name.Trim(); entity.SortOrder = request.SortOrder; entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, SortOrder = entity.SortOrder };
    }

    public async Task DeactivateMasterAsync(string type, Guid id, string? user)
    {
        switch (type.ToLowerInvariant())
        {
            case "departments":
                var d = await _db.Departments.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                EnsureInUserLocation(d.LocationId, "department");
                d.IsActive = false; d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = user; break;
            case "purposes":
                var p = await _db.VisitPurposes.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                EnsureInUserLocation(p.LocationId, "purpose");
                p.IsActive = false; p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = user; break;
            case "locations":
                var l = await _db.Locations.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                if (_tenant.LocationId is not null)
                    throw new UnauthorizedAccessException("Only a Super Admin can deactivate a location.");
                l.IsActive = false; l.UpdatedAt = DateTime.UtcNow; l.UpdatedBy = user; break;
            case "idtypes":
                var i = await _db.IdTypes.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                i.IsActive = false; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = user; break;
            case "meeting-halls":
                var h = await _db.MeetingHalls.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                EnsureInUserLocation(h.LocationId, "meeting hall");
                h.IsActive = false; h.UpdatedAt = DateTime.UtcNow; h.UpdatedBy = user; break;
            default: throw new InvalidOperationException("Unknown master type");
        }
        await _db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<MasterItemDto>> GetSimpleAsync<T>(DbSet<T> set, bool activeOnly) where T : class
    {
        // fallback unused - kept for compile simplicity with purpose/idtype via direct queries
        await Task.CompletedTask;
        return Array.Empty<MasterItemDto>();
    }

    private async Task<IReadOnlyList<MasterItemDto>> GetSimpleAsync(DbSet<VisitPurpose> set, bool activeOnly)
    {
        var q = set.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto
        {
            Id = x.Id,
            Name = x.Name,
            Code = x.Code,
            IsActive = x.IsActive,
            SortOrder = x.SortOrder,
        }).ToListAsync();
    }

    private async Task<IReadOnlyList<MasterItemDto>> GetSimpleAsync(DbSet<IdType> set, bool activeOnly)
    {
        var q = set.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, SortOrder = x.SortOrder }).ToListAsync();
    }
}

public interface IReportService
{
    Task<object> GenerateAsync(ReportRequest request, string generatedBy);
    Task<byte[]> ExportExcelAsync(ReportRequest request, string generatedBy);
    Task<byte[]> ExportPdfAsync(ReportRequest request, string generatedBy);
    Task<byte[]> ExportCsvAsync(ReportRequest request, string generatedBy);
}

public class ReportService : IReportService
{
    internal const string AutoCheckOutUser = "system:auto-checkout-24h";
    internal const string AutoCheckOutNote = "Auto check-out after 24 hours";

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ISettingsService _settings;

    public ReportService(ApplicationDbContext db, IWebHostEnvironment env, ISettingsService settings)
    {
        _db = db;
        _env = env;
        _settings = settings;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static bool IsDefaulterReport(ReportRequest request) =>
        request.ReportType.Equals("defaulters", StringComparison.OrdinalIgnoreCase)
        || request.ReportType.Equals("checkoutdefaulters", StringComparison.OrdinalIgnoreCase);

    private static string ReportTitle(ReportRequest request) =>
        IsDefaulterReport(request) ? "TIAANO Check-out Defaulters Report" : "TIAANO Visitor Report";

    public async Task<object> GenerateAsync(ReportRequest request, string generatedBy)
    {
        var (rows, allowedMinutes) = await QueryRows(request);
        var page = request.Page.HasValue ? Math.Max(1, request.Page.Value) : (int?)null;
        var pageSize = page.HasValue ? Math.Clamp(request.PageSize ?? 20, 1, 500) : (int?)null;
        var pageRows = page.HasValue
            ? rows.Skip((page.Value - 1) * pageSize!.Value).Take(pageSize.Value).ToList()
            : rows;
        return new
        {
            reportType = request.ReportType,
            generatedAt = AppTime.Now,
            generatedBy,
            total = rows.Count,
            manualCheckOutCount = rows.Count(r => r.IsManualCheckOut),
            defaulterCount = rows.Count(r => r.DefaulterReason.Length > 0),
            allowedVisitMinutes = allowedMinutes,
            page,
            pageSize,
            rows = pageRows
        };
    }

    public async Task<byte[]> ExportExcelAsync(ReportRequest request, string generatedBy)
    {
        var (rows, allowedMinutes) = await QueryRows(request);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(IsDefaulterReport(request) ? "Defaulters" : "Visitors");

        var headerRow = 1;
        var logoBytes = TryReadLogoBytes();
        if (logoBytes is { Length: > 0 })
        {
            using var logoStream = new MemoryStream(logoBytes);
            ws.AddPicture(logoStream)
                .MoveTo(ws.Cell(1, 1))
                .WithSize(140, 48);
            ws.Row(1).Height = 40;
            ws.Row(2).Height = 8;
            headerRow = 3;
        }

        ws.Cell(headerRow, 1).Value = ReportTitle(request);
        ws.Cell(headerRow, 1).Style.Font.Bold = true;
        ws.Cell(headerRow, 1).Style.Font.FontSize = 16;
        ws.Cell(headerRow + 1, 1).Value = $"Generated: {AppTime.Now:yyyy-MM-dd HH:mm}";
        ws.Cell(headerRow + 2, 1).Value = $"Generated By: {generatedBy}";
        ws.Cell(headerRow + 3, 1).Value =
            $"Manual check-outs: {rows.Count(r => r.IsManualCheckOut)} | Check-out defaulters: {rows.Count(r => r.DefaulterReason.Length > 0)} | Allowed visit duration: {FormatDuration(allowedMinutes)}";
        var tableHeader = headerRow + 5;
        var headers = new[]
        {
            "Visit #", "Visitor", "Company", "Person to meet", "Department", "Date", "Status",
            "Manual check-out", "Purposes", "Locations", "Feedback avg", "Feedback ratings", "Feedback comments",
            "Phone", "Check-in", "Check-out", "Duration", "Check-out issue"
        };
        for (var i = 0; i < headers.Length; i++) ws.Cell(tableHeader, i + 1).Value = headers[i];
        ws.Range(tableHeader, 1, tableHeader, headers.Length).Style.Font.Bold = true;
        var r = tableHeader + 1;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.VisitNumber;
            ws.Cell(r, 2).Value = row.VisitorName;
            ws.Cell(r, 3).Value = row.Company;
            ws.Cell(r, 4).Value = row.Host;
            ws.Cell(r, 5).Value = row.Department;
            ws.Cell(r, 6).Value = row.VisitDate;
            ws.Cell(r, 7).Value = row.Status;
            ws.Cell(r, 8).Value = row.IsManualCheckOut ? "Yes" : "No";
            ws.Cell(r, 9).Value = row.Purposes;
            ws.Cell(r, 10).Value = row.Locations;
            ws.Cell(r, 11).Value = row.FeedbackAvg;
            ws.Cell(r, 12).Value = row.FeedbackRatings;
            ws.Cell(r, 13).Value = row.FeedbackComments;
            ws.Cell(r, 14).Value = row.Phone;
            ws.Cell(r, 15).Value = row.CheckInAt;
            ws.Cell(r, 16).Value = row.CheckOutAt;
            ws.Cell(r, 17).Value = row.Duration;
            ws.Cell(r, 18).Value = row.DefaulterReason;
            r++;
        }
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportPdfAsync(ReportRequest request, string generatedBy)
    {
        var (rows, allowedMinutes) = await QueryRows(request);
        var manualCount = rows.Count(r => r.IsManualCheckOut);
        var defaulterCount = rows.Count(r => r.DefaulterReason.Length > 0);
        var defaulters = IsDefaulterReport(request);
        var logoBytes = TryReadLogoBytes();
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                if (defaulters) page.Size(PageSizes.A4.Landscape());
                page.Header().Row(row =>
                {
                    if (logoBytes is { Length: > 0 })
                    {
                        row.ConstantItem(90).Height(32).Image(logoBytes).FitArea();
                        row.ConstantItem(12);
                    }
                    row.RelativeItem().AlignMiddle().Text(ReportTitle(request)).Bold().FontSize(16);
                });
                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(8).Text($"Generated: {AppTime.Now:yyyy-MM-dd HH:mm} by {generatedBy}");
                    col.Item().Text(defaulters
                        ? $"Records: {rows.Count} | Allowed visit duration: {FormatDuration(allowedMinutes)}"
                        : $"Report: {request.ReportType} | Records: {rows.Count} | Manual check-outs: {manualCount} | Check-out defaulters: {defaulterCount}").FontSize(10);
                    if (defaulters)
                    {
                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(1.4f); c.RelativeColumn(2); c.RelativeColumn(1.3f);
                                c.RelativeColumn(1.8f); c.RelativeColumn(1.5f); c.RelativeColumn(1.5f);
                                c.RelativeColumn(1.5f); c.RelativeColumn(1); c.RelativeColumn(3);
                            });
                            table.Header(h =>
                            {
                                foreach (var title in new[] { "Visit #", "Visitor", "Phone", "Person to meet", "Department", "Check-in", "Check-out", "Duration", "Check-out issue" })
                                    h.Cell().Text(title).Bold().FontSize(9);
                            });
                            foreach (var row in rows.Take(500))
                            {
                                table.Cell().Text(row.VisitNumber).FontSize(8);
                                table.Cell().Text(row.VisitorName).FontSize(8);
                                table.Cell().Text(row.Phone).FontSize(8);
                                table.Cell().Text(row.Host).FontSize(8);
                                table.Cell().Text(row.Department).FontSize(8);
                                table.Cell().Text(row.CheckInAt).FontSize(8);
                                table.Cell().Text(string.IsNullOrEmpty(row.CheckOutAt) ? "Still inside" : row.CheckOutAt).FontSize(8);
                                table.Cell().Text(row.Duration).FontSize(8);
                                table.Cell().Text(row.DefaulterReason).FontSize(8);
                            }
                        });
                        return;
                    }
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2);
                            c.RelativeColumn(1.5f); c.RelativeColumn(1); c.RelativeColumn(1);
                            c.RelativeColumn(1); c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Visitor").Bold();
                            h.Cell().Text("Company").Bold();
                            h.Cell().Text("Person to meet").Bold();
                            h.Cell().Text("Department").Bold();
                            h.Cell().Text("Date").Bold();
                            h.Cell().Text("Status").Bold();
                            h.Cell().Text("Manual").Bold();
                            h.Cell().Text("Feedback").Bold();
                        });
                        foreach (var row in rows.Take(200))
                        {
                            table.Cell().Text(row.VisitorName).FontSize(9);
                            table.Cell().Text(row.Company).FontSize(9);
                            table.Cell().Text(row.Host).FontSize(9);
                            table.Cell().Text(row.Department).FontSize(9);
                            table.Cell().Text(row.VisitDate).FontSize(9);
                            table.Cell().Text(row.Status).FontSize(9);
                            table.Cell().Text(row.IsManualCheckOut ? "Yes" : "No").FontSize(9);
                            table.Cell().Text(string.IsNullOrWhiteSpace(row.FeedbackAvg) ? "—" : $"{row.FeedbackAvg}★").FontSize(9);
                        }
                    });
                });
            });
        });
        return doc.GeneratePdf();
    }

    public async Task<byte[]> ExportCsvAsync(ReportRequest request, string generatedBy)
    {
        var (rows, allowedMinutes) = await QueryRows(request);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {ReportTitle(request)} generated {AppTime.Now:yyyy-MM-dd HH:mm} by {generatedBy}");
        sb.AppendLine($"# Manual check-outs: {rows.Count(r => r.IsManualCheckOut)} | Check-out defaulters: {rows.Count(r => r.DefaulterReason.Length > 0)} | Allowed visit duration: {FormatDuration(allowedMinutes)}");
        sb.AppendLine("VisitNumber,Visitor,Company,Person to meet,Department,Date,Status,ManualCheckOut,Purposes,Locations,FeedbackAvg,FeedbackRatings,FeedbackComments,Phone,CheckIn,CheckOut,Duration,CheckOutIssue");
        foreach (var row in rows)
        {
            sb.AppendLine($"\"{row.VisitNumber}\",\"{row.VisitorName}\",\"{row.Company}\",\"{row.Host}\",\"{row.Department}\",\"{row.VisitDate}\",\"{row.Status}\",\"{(row.IsManualCheckOut ? "Yes" : "No")}\",\"{row.Purposes}\",\"{row.Locations}\",\"{row.FeedbackAvg}\",\"{EscapeCsv(row.FeedbackRatings)}\",\"{EscapeCsv(row.FeedbackComments)}\",\"{row.Phone}\",\"{row.CheckInAt}\",\"{row.CheckOutAt}\",\"{row.Duration}\",\"{EscapeCsv(row.DefaulterReason)}\"");
        }
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value) =>
        (value ?? "").Replace("\"", "\"\"");

    private async Task<(List<ReportRow> Rows, int AllowedMinutes)> QueryRows(ReportRequest request)
    {
        var settings = await _settings.GetAsync();
        var allowedMinutes = settings.MaxVisitDurationWarningMinutes > 0 ? settings.MaxVisitDurationWarningMinutes : 240;
        var nowUtc = DateTime.UtcNow;
        var overdueCutoff = nowUtc.AddMinutes(-allowedMinutes);

        var from = request.DateFrom ?? AppTime.Today.AddDays(-30);
        var to = request.DateTo ?? AppTime.Today;
        var q = _db.VisitorVisits
            .Include(v => v.Visitor)
            .Include(v => v.Department)
            .Include(v => v.HostEmployee)
            .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
            .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
            .AsNoTracking()
            .Where(v => v.VisitDate >= from && v.VisitDate <= to);

        if (request.DepartmentId.HasValue) q = q.Where(v => v.DepartmentId == request.DepartmentId);
        if (request.HostEmployeeId.HasValue) q = q.Where(v => v.HostEmployeeId == request.HostEmployeeId);
        if (!string.IsNullOrWhiteSpace(request.Company)) q = q.Where(v => v.Visitor.CompanyName.Contains(request.Company));
        if (request.PurposeId.HasValue) q = q.Where(v => v.VisitPurposes.Any(p => p.VisitPurposeId == request.PurposeId));
        if (request.LocationId.HasValue) q = q.Where(v => v.VisitLocations.Any(l => l.LocationId == request.LocationId));
        if (request.Status.HasValue) q = q.Where(v => v.Status == request.Status);

        switch (request.ReportType.ToLowerInvariant())
        {
            case "inside":
            case "currentlyinside":
                q = q.Where(v => v.Status == VisitStatus.Inside); break;
            case "rejected":
                q = q.Where(v => v.Status == VisitStatus.Rejected); break;
            case "entryexit":
                q = q.Where(v => v.CheckInAt != null); break;
            case "manualcheckout":
            case "manualcheckouts":
                q = q.Where(v => v.IsManualCheckOut); break;
            case "defaulters":
            case "checkoutdefaulters":
                q = q.Where(v => v.CheckInAt != null && (
                    (v.Status == VisitStatus.Inside && v.CheckInAt < overdueCutoff)
                    || v.IsManualCheckOut
                    || (v.CheckOutAt != null && EF.Functions.DateDiffMinute(v.CheckInAt, v.CheckOutAt) > allowedMinutes)));
                break;
        }

        var list = await q.OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.VisitTime).Take(5000).ToListAsync();
        var visitIds = list.Select(v => v.Id).ToList();
        var feedbackByVisit = await _db.VisitFeedbacks.AsNoTracking()
            .Include(f => f.Answers)
            .Where(f => visitIds.Contains(f.VisitorVisitId))
            .ToDictionaryAsync(f => f.VisitorVisitId);

        var rows = list.Select(v =>
        {
            feedbackByVisit.TryGetValue(v.Id, out var fb);
            var checkIn = VisitorService.AsUtc(v.CheckInAt);
            var checkOut = VisitorService.AsUtc(v.CheckOutAt);
            int? durationMinutes = checkIn is null
                ? null
                : (int)((checkOut ?? (v.Status == VisitStatus.Inside ? nowUtc : checkIn.Value)) - checkIn.Value).TotalMinutes;
            var avg = fb?.Answers is { Count: > 0 }
                ? fb.Answers.Average(a => a.Rating).ToString("0.0")
                : "";
            var ratings = fb is null
                ? ""
                : string.Join("; ", fb.Answers.OrderBy(a => a.QuestionText).Select(a => $"{a.QuestionText}: {a.Rating}/5"));
            return new ReportRow(
                v.VisitNumber,
                v.Visitor.FullName,
                v.Visitor.CompanyName,
                v.HostEmployee.FullName,
                v.Department.Name,
                v.VisitDate.ToString("yyyy-MM-dd"),
                v.Status.ToString(),
                v.IsManualCheckOut,
                string.Join("; ", v.VisitPurposes.Select(p => p.VisitPurpose.Name)),
                string.Join("; ", v.VisitLocations.Select(l => l.Location.Name)),
                avg,
                ratings,
                fb?.Comments ?? "",
                v.Visitor.Phone ?? "",
                FormatLocal(checkIn),
                FormatLocal(checkOut),
                durationMinutes is int d ? FormatDuration(d) : "",
                DefaulterReason(v, durationMinutes, allowedMinutes)
            );
        }).ToList();

        if (IsDefaulterReport(request))
            rows = rows.Where(r => r.DefaulterReason.Length > 0).ToList();
        return (SortRows(rows, request.SortBy, request.SortDir), allowedMinutes);
    }

    /// <summary>Sorts the whole result before paging; blank values always go last. Unknown keys keep newest first.</summary>
    private static List<ReportRow> SortRows(List<ReportRow> rows, string? sortBy, string? sortDir)
    {
        Func<ReportRow, string>? key = (sortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "visitnumber" => r => r.VisitNumber,
            "visitor" => r => r.VisitorName,
            "company" => r => r.Company,
            "host" => r => r.Host,
            "department" => r => r.Department,
            "date" => r => r.VisitDate,
            "status" => r => r.Status,
            "manual" => r => r.IsManualCheckOut ? "1" : "0",
            "location" => r => r.Locations,
            "feedback" => r => r.FeedbackAvg,
            "phone" => r => r.Phone,
            "checkin" => r => r.CheckInAt,
            "checkout" => r => r.CheckOutAt,
            "issue" => r => r.DefaulterReason,
            _ => null,
        };
        if (key is null) return rows;
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var filled = rows.Where(r => key(r).Length > 0);
        var ordered = desc
            ? filled.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : filled.OrderBy(key, StringComparer.OrdinalIgnoreCase);
        return ordered.Concat(rows.Where(r => key(r).Length == 0)).ToList();
    }

    private static string DefaulterReason(VisitorVisit v, int? durationMinutes, int allowedMinutes)
    {
        if (v.CheckInAt is null || durationMinutes is not int minutes)
            return "";
        var over = minutes - allowedMinutes;
        if (v.Status == VisitStatus.Inside)
            return over > 0 ? $"Not checked out - still inside, overdue by {FormatDuration(over)}" : "";
        var auto = v.UpdatedBy == AutoCheckOutUser
                   || (v.Notes?.Contains(AutoCheckOutNote, StringComparison.OrdinalIgnoreCase) ?? false);
        if (auto)
            return "Did not check out - auto checked out by system after 24 hours";
        if (v.IsManualCheckOut)
            return "Did not check out - checked out manually by admin";
        if (v.CheckOutAt is not null && over > 0)
            return $"Late check-out - overstayed by {FormatDuration(over)}";
        return "";
    }

    private static string FormatLocal(DateTime? utc) =>
        utc is DateTime value ? AppTime.ToLocal(value).ToString("yyyy-MM-dd HH:mm") : "";

    private static string FormatDuration(int minutes)
    {
        if (minutes < 60) return $"{minutes}m";
        var days = minutes / 1440;
        var hours = minutes % 1440 / 60;
        var mins = minutes % 60;
        return days > 0 ? $"{days}d {hours}h {mins}m" : $"{hours}h {mins}m";
    }

    /// <summary>
    /// Loads the TIAANO mark for branded exports. Missing files are ignored so exports still work
    /// on machines that have not copied branding assets into wwwroot.
    /// </summary>
    private byte[]? TryReadLogoBytes()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("wwwroot", "branding", "tiaano-logo.png"),
                     Path.Combine("branding", "tiaano-logo.png")
                 })
        {
            var path = Path.Combine(_env.ContentRootPath, relative);
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }

        var webRoot = Path.Combine(_env.WebRootPath ?? string.Empty, "branding", "tiaano-logo.png");
        return File.Exists(webRoot) ? File.ReadAllBytes(webRoot) : null;
    }

    private record ReportRow(
        string VisitNumber,
        string VisitorName,
        string Company,
        string Host,
        string Department,
        string VisitDate,
        string Status,
        bool IsManualCheckOut,
        string Purposes,
        string Locations,
        string FeedbackAvg,
        string FeedbackRatings,
        string FeedbackComments,
        string Phone,
        string CheckInAt,
        string CheckOutAt,
        string Duration,
        string DefaulterReason);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> GetUsersAsync();
    Task<UserDto> CreateAsync(CreateUserRequest request, string? actor);
    Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string? actor);
    /// <summary>Creates a Host login for an employee and links it, so they see their own visitors and can add MoM.</summary>
    Task<EmployeeDto> CreateLoginForEmployeeAsync(Guid employeeId, CreateEmployeeLoginRequest request, string? actor);
    Task ResetPasswordAsync(string id, AdminResetPasswordRequest request, bool actorIsSuperAdmin, string? actor);
}

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEntitlementService _entitlements;

    public UserAdminService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ITenantContext tenant,
        IEntitlementService entitlements)
    {
        _userManager = userManager;
        _db = db;
        _tenant = tenant;
        _entitlements = entitlements;
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync()
    {
        var query = _userManager.Users
            .Include(u => u.Department)
            .Include(u => u.Location)
            .AsQueryable();
        if (_tenant.LocationId is Guid scope)
            query = query.Where(u => u.LocationId == scope);
        var users = await query.OrderBy(u => u.FullName).ToListAsync();
        var faceCounts = await _db.UserFaceLogins
            .GroupBy(f => f.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);
        var employeeByUser = new Dictionary<string, Guid>();
        foreach (var link in await _db.Employees.Where(e => e.UserId != null).Select(e => new { e.UserId, e.Id }).ToListAsync())
            employeeByUser.TryAdd(link.UserId!, link.Id);
        var result = new List<UserDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var primaryRole = roles.FirstOrDefault() ?? string.Empty;
            var allLocations = roles.Contains(AppRoles.SuperAdmin);
            result.Add(new UserDto
            {
                Id = u.Id,
                Username = u.UserName ?? "",
                FullName = u.FullName,
                Email = u.Email ?? "",
                Roles = roles.ToList(),
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department?.Name,
                LocationId = allLocations ? null : u.LocationId,
                LocationName = allLocations ? null : u.Location?.Name,
                AllowedMenuKeys = MenuAccess.Deserialize(u.AllowedMenuKeysJson, primaryRole),
                MustChangePassword = u.MustChangePassword,
                IsActive = u.IsActive,
                FaceLoginCount = faceCounts.GetValueOrDefault(u.Id),
                EmployeeId = employeeByUser.TryGetValue(u.Id, out var employeeId) ? employeeId : null
            });
        }
        return result;
    }

    /// <summary>
    /// Binding a user to a location controls what they can see, so the target location must belong to the
    /// caller's tenant and still be active.
    /// </summary>
    private async Task<Guid?> ValidateLocationAsync(Guid? locationId)
    {
        if (locationId is not Guid id || id == Guid.Empty) return null;

        var exists = await _db.Locations.AnyAsync(l => l.Id == id && l.IsActive);
        if (!exists)
            throw new InvalidOperationException("Selected location was not found or is inactive.");

        return id;
    }

    /// <summary>
    /// Super Admins span every location, so they carry none. Admin and desk roles must have one, and an
    /// Admin (who is bound to a location) can only hand out their own location and never Super Admin.
    /// </summary>
    private async Task<Guid?> ResolveUserLocationAsync(string role, Guid? requestedLocationId)
    {
        if (_tenant.LocationId is Guid scope)
        {
            if (string.Equals(role, AppRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Only a Super Admin can give the Super Admin role.");
            if (requestedLocationId is Guid asked && asked != Guid.Empty && asked != scope)
                throw new UnauthorizedAccessException("You can only manage users at your own location.");
            requestedLocationId = scope;
        }

        if (string.Equals(role, AppRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            return null;

        var locationId = await ValidateLocationAsync(requestedLocationId);
        if (locationId is null && AppRoles.LocationBound.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a location. Only Super Admins can work across all locations.");
        return locationId;
    }

    /// <summary>An Admin may only manage users at their own location, and never a Super Admin.</summary>
    private async Task EnsureCanManageUserAsync(ApplicationUser target)
    {
        if (_tenant.LocationId is not Guid scope) return;
        if (target.LocationId != scope || await _userManager.IsInRoleAsync(target, AppRoles.SuperAdmin))
            throw new UnauthorizedAccessException("You can only manage users at your own location.");
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, string? actor)
    {
        if (!AppRoles.All.Contains(request.Role))
            throw new InvalidOperationException("Invalid role.");
        var locationId = await ResolveUserLocationAsync(request.Role, request.LocationId);
        await _entitlements.EnsureCanCreateUserAsync();
        var tenantId = _tenant.TenantId is Guid tid && tid != Guid.Empty
            ? tid
            : throw new UnauthorizedAccessException("Tenant context is required.");
        var menus = MenuAccess.NormalizeForRole(request.Role, request.AllowedMenuKeys);
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            TenantId = tenantId,
            DepartmentId = request.DepartmentId,
            LocationId = locationId,
            AllowedMenuKeysJson = MenuAccess.Serialize(menus),
            IsActive = true,
            MustChangePassword = true,
            EmailConfirmed = true,
            CreatedBy = actor
        };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await _userManager.AddToRoleAsync(user, request.Role);
        return (await GetUsersAsync()).First(u => u.Id == user.Id);
    }

    public async Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string? actor)
    {
        var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new InvalidOperationException("User not found.");
        if (!AppRoles.All.Contains(request.Role))
            throw new InvalidOperationException("Invalid role.");
        await EnsureCanManageUserAsync(user);
        var locationId = await ResolveUserLocationAsync(request.Role, request.LocationId);
        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim();
        user.DepartmentId = request.DepartmentId;
        user.LocationId = locationId;
        user.AllowedMenuKeysJson = MenuAccess.Serialize(
            MenuAccess.NormalizeForRole(request.Role, request.AllowedMenuKeys));
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = actor;
        await _userManager.UpdateAsync(user);
        var roles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, roles);
        await _userManager.AddToRoleAsync(user, request.Role);
        return (await GetUsersAsync()).First(u => u.Id == user.Id);
    }

    public async Task ResetPasswordAsync(string id, AdminResetPasswordRequest request, bool actorIsSuperAdmin, string? actor)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new InvalidOperationException("User not found.");
        if (!actorIsSuperAdmin && await _userManager.IsInRoleAsync(user, AppRoles.SuperAdmin))
            throw new UnauthorizedAccessException("Only a Super Admin can reset a Super Admin's password.");
        await EnsureCanManageUserAsync(user);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = request.MustChangePassword;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = actor;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<EmployeeDto> CreateLoginForEmployeeAsync(Guid employeeId, CreateEmployeeLoginRequest request, string? actor)
    {
        var employee = await _db.Employees
            .Include(e => e.Department)
            .Include(e => e.Location)
            .FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException("Employee not found.");
        if (_tenant.LocationId is Guid scope && employee.LocationId != scope)
            throw new UnauthorizedAccessException("You can only create logins for employees at your own location.");
        if (!employee.IsActive)
            throw new InvalidOperationException("Activate the employee before creating a login.");
        if (!string.IsNullOrWhiteSpace(employee.UserId)
            && await _userManager.Users.AnyAsync(u => u.Id == employee.UserId))
            throw new InvalidOperationException("This employee already has a login.");

        var email = string.IsNullOrWhiteSpace(request.Email) ? employee.Email?.Trim() : request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required. Add it to the employee or enter it for the login.");

        var role = AppRoles.All.FirstOrDefault(r => string.Equals(r, request.Role?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? (string.IsNullOrWhiteSpace(request.Role) ? AppRoles.Host : throw new InvalidOperationException("Invalid role."));
        if (employee.LocationId is null && AppRoles.LocationBound.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{employee.FullName} has no location. Set their location in Masters → Employees first; {role} logins work at one location.");

        var created = await CreateAsync(new CreateUserRequest
        {
            FullName = employee.FullName,
            Username = request.Username,
            Email = email,
            Role = role,
            DepartmentId = employee.DepartmentId,
            LocationId = employee.LocationId,
            AllowedMenuKeys = request.AllowedMenuKeys,
            Password = request.Password
        }, actor);

        employee.UserId = created.Id;
        employee.UpdatedAt = DateTime.UtcNow;
        employee.UpdatedBy = actor;
        await _db.SaveChangesAsync();

        return new EmployeeDto
        {
            Id = employee.Id,
            FullName = employee.FullName,
            Code = employee.Code,
            Email = employee.Email,
            Phone = employee.Phone,
            Intercom = employee.Intercom,
            Designation = employee.Designation,
            DepartmentId = employee.DepartmentId,
            DepartmentName = employee.Department.Name,
            LocationId = employee.LocationId,
            LocationName = employee.Location?.Name,
            UserId = employee.UserId,
            UserName = created.Username,
            IsActive = employee.IsActive
        };
    }
}
