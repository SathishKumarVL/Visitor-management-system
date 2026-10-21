using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// The Employee menu: full employee profiles with an enrolled face. A location-bound Admin only sees and
/// changes employees of their own location.
/// </summary>
public interface IEmployeeMasterService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid? locationId, Guid? departmentId, bool activeOnly);
    Task<EmployeeDto> UpsertAsync(Guid? id, EmployeeProfileRequest request, string? actor);
    Task DeactivateAsync(Guid id, string? actor);
    Task DeleteAsync(Guid id);
}

public class EmployeeMasterService : IEmployeeMasterService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IUserFaceLoginService _faces;
    private readonly IMediaStorageService _media;

    public EmployeeMasterService(
        ApplicationDbContext db,
        ITenantContext tenant,
        IUserFaceLoginService faces,
        IMediaStorageService media)
    {
        _db = db;
        _tenant = tenant;
        _faces = faces;
        _media = media;
    }

    private Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid? locationId, Guid? departmentId, bool activeOnly)
    {
        if (_tenant.LocationId is Guid scope)
        {
            if (locationId.HasValue && locationId.Value != scope) return [];
            locationId = scope;
        }

        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(e => e.IsActive);
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        if (locationId.HasValue) q = q.Where(e => e.LocationId == locationId);
        return await ProjectAsync(q.OrderBy(e => e.FullName));
    }

    private async Task<List<EmployeeDto>> ProjectAsync(IQueryable<Employee> q)
    {
        var rows = await q.Select(e => new
        {
            Dto = new EmployeeDto
            {
                Id = e.Id,
                Title = e.Title,
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
            },
            e.PhotoPath
        }).ToListAsync();

        foreach (var row in rows)
            row.Dto.PhotoUrl = row.PhotoPath is null ? null : _media.ToPublicApiPath(row.PhotoPath);
        return rows.Select(r => r.Dto).ToList();
    }

    public async Task<EmployeeDto> UpsertAsync(Guid? id, EmployeeProfileRequest request, string? actor)
    {
        Employee entity;
        if (id.HasValue)
        {
            entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id.Value)
                ?? throw new InvalidOperationException("Employee not found.");
            EnsureInScope(entity.LocationId);
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = actor;
        }
        else
        {
            entity = new Employee { TenantId = CurrentTenantId, CreatedBy = actor };
        }

        var title = EmployeeTitles.All.FirstOrDefault(t => string.Equals(t, request.Title?.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select Mr or Ms.");

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length == 0) throw new InvalidOperationException("Name is required.");

        var code = (request.Code ?? string.Empty).Trim();
        if (code.Length == 0) throw new InvalidOperationException("Employee ID is required.");
        var codeOwner = await _db.Employees.AsNoTracking()
            .Where(e => e.Code == code && e.Id != entity.Id)
            .Select(e => e.FullName)
            .FirstOrDefaultAsync();
        if (codeOwner is not null)
            throw new InvalidOperationException($"Employee ID '{code}' is already used by {codeOwner}.");

        if (request.DateOfBirth is not DateOnly dob) throw new InvalidOperationException("Date of birth is required.");
        if (dob > DateOnly.FromDateTime(DateTime.Today)) throw new InvalidOperationException("Date of birth cannot be in the future.");
        if (dob.Year < 1900) throw new InvalidOperationException("Enter a valid date of birth.");

        if (request.Gender is not { } gender || !Enum.IsDefined(gender))
            throw new InvalidOperationException("Sex is required.");

        var phone = NormalizePhone(request.Phone);

        var email = (request.Email ?? string.Empty).Trim();
        if (email.Length == 0) throw new InvalidOperationException("Email is required.");
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new InvalidOperationException("Enter a valid email address.");

        var locationId = request.LocationId;
        if (_tenant.LocationId is Guid scope)
        {
            if (locationId.HasValue && locationId.Value != scope)
                throw new UnauthorizedAccessException("You can only add employees to your own location.");
            locationId = scope;
        }
        if (locationId.HasValue && !await _db.Locations.AnyAsync(l => l.Id == locationId.Value))
            throw new InvalidOperationException("Selected location was not found.");
        if (request.DepartmentId == Guid.Empty) throw new InvalidOperationException("Department is required.");
        if (!await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId))
            throw new InvalidOperationException("Selected department was not found.");

        var hasNewFace = !string.IsNullOrWhiteSpace(request.FacePhotoBase64);
        if (!hasNewFace && entity.FaceDescriptor is null)
            throw new InvalidOperationException("Capture the employee's face.");

        entity.Title = title;
        entity.FullName = fullName;
        entity.Code = code;
        entity.DateOfBirth = dob;
        entity.Gender = gender;
        entity.Phone = phone;
        entity.Email = email;
        entity.Designation = string.IsNullOrWhiteSpace(request.Designation) ? null : request.Designation.Trim();
        entity.Intercom = string.IsNullOrWhiteSpace(request.Intercom) ? null : request.Intercom.Trim();
        entity.DepartmentId = request.DepartmentId;
        entity.LocationId = locationId;
        entity.IsActive = !id.HasValue || request.IsActive;

        if (hasNewFace)
        {
            var face = await _faces.BuildEmployeeFaceAsync(request.FacePhotoBase64!, id, entity.UserId);
            entity.PhotoPath = await _media.SaveVisitorPhotoAsync(entity.Id, face.PhotoBytes);
            entity.FaceDescriptor = face.Descriptor;
            entity.FaceDimensions = face.Dimensions;
            entity.FaceModel = face.Model;
        }

        if (!id.HasValue) _db.Employees.Add(entity);
        await DesignationMaster.EnsureAsync(_db, entity.TenantId, entity.Designation, actor);
        await _db.SaveChangesAsync();
        if (hasNewFace && entity.UserId is not null)
            await _faces.ReplaceLoginFaceFromEmployeeAsync(entity.Id, entity.UserId, actor);

        return (await ProjectAsync(_db.Employees.AsNoTracking().Where(e => e.Id == entity.Id))).Single();
    }

    public async Task DeactivateAsync(Guid id, string? actor)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new InvalidOperationException("Employee not found.");
        EnsureInScope(entity.LocationId);
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = actor;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Permanently removes an employee who was never a visiting person; anyone with visits can only be
    /// deactivated. Their login account is kept and simply unlinked.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new InvalidOperationException("Employee not found.");
        EnsureInScope(entity.LocationId);

        var tenantId = CurrentTenantId;
        var visits = await _db.VisitorVisits.IgnoreQueryFilters()
            .CountAsync(v => v.TenantId == tenantId && v.HostEmployeeId == id);
        var approvals = await _db.Approvals.CountAsync(a => a.HostEmployeeId == id);
        if (visits + approvals > 0)
            throw new InvalidOperationException(
                $"'{entity.FullName}' cannot be deleted because they are the visiting person on {visits} visit{(visits == 1 ? "" : "s")}. Deactivate them instead.");

        var photo = entity.PhotoPath;
        _db.Employees.Remove(entity);
        await _db.SaveChangesAsync();
        if (!string.IsNullOrWhiteSpace(photo)) _media.Delete(photo);
    }

    private void EnsureInScope(Guid? recordLocationId)
    {
        if (_tenant.LocationId is Guid scope && recordLocationId != scope)
            throw new UnauthorizedAccessException("This employee belongs to another location.");
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("Mobile number is required.");
        var digits = Regex.Replace(phone.Trim(), @"\D", "");
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        if (digits.Length != 10) throw new InvalidOperationException("Mobile number must be exactly 10 digits.");
        return digits;
    }
}
