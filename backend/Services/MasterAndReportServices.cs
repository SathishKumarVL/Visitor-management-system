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
    Task<IReadOnlyList<MasterItemDto>> GetDepartmentsAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertDepartmentAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<EmployeeDto>> GetHostsAsync(Guid? departmentId = null, bool activeOnly = true);
    Task<EmployeeDto> UpsertEmployeeAsync(Guid? id, CreateEmployeeRequest request, string? user);
    Task DeactivateEmployeeAsync(Guid id, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetPurposesAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertPurposeAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetLocationsAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertLocationAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetIdTypesAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertIdTypeAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetEntryGatesAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertEntryGateAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task<IReadOnlyList<MasterItemDto>> GetExitGatesAsync(bool activeOnly = true);
    Task<MasterItemDto> UpsertExitGateAsync(Guid? id, MasterUpsertRequest request, string? user);
    Task DeactivateMasterAsync(string type, Guid id, string? user);
}

public class MasterDataService : IMasterDataService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenant;

    public MasterDataService(ApplicationDbContext db, IAuditService audit, ITenantContext tenant)
    {
        _db = db;
        _audit = audit;
        _tenant = tenant;
    }

    private Guid CurrentTenantId => _tenant.TenantId ?? WellKnownTenants.TiaanoId;

    public async Task<IReadOnlyList<MasterItemDto>> GetDepartmentsAsync(bool activeOnly = true)
    {
        var q = _db.Departments.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new MasterItemDto { Id = x.Id, Name = x.Name, Code = x.Code, Intercom = x.Intercom, IsActive = x.IsActive, SortOrder = x.SortOrder })
            .ToListAsync();
    }

    public async Task<MasterItemDto> UpsertDepartmentAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        Department entity;
        if (id.HasValue)
        {
            entity = await _db.Departments.FindAsync(id.Value) ?? throw new InvalidOperationException("Department not found.");
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new Department { TenantId = CurrentTenantId, CreatedBy = user };
            _db.Departments.Add(entity);
        }
        entity.Name = request.Name.Trim();
        entity.Code = request.Code;
        entity.Intercom = request.Intercom;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(id.HasValue ? "DepartmentUpdated" : "DepartmentCreated", "Department", entity.Id.ToString(), entity.Name);
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, Code = entity.Code, Intercom = entity.Intercom, IsActive = entity.IsActive, SortOrder = entity.SortOrder };
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetHostsAsync(Guid? departmentId = null, bool activeOnly = true)
    {
        var q = _db.Employees.Include(e => e.Department).AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(e => e.IsActive);
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        return await q.OrderBy(e => e.FullName).Select(e => new EmployeeDto
        {
            Id = e.Id,
            FullName = e.FullName,
            Email = e.Email,
            Phone = e.Phone,
            Intercom = e.Intercom,
            Designation = e.Designation,
            DepartmentId = e.DepartmentId,
            DepartmentName = e.Department.Name,
            UserId = e.UserId,
            IsActive = e.IsActive
        }).ToListAsync();
    }

    public async Task<EmployeeDto> UpsertEmployeeAsync(Guid? id, CreateEmployeeRequest request, string? user)
    {
        Employee entity;
        if (id.HasValue)
        {
            entity = await _db.Employees.Include(e => e.Department).FirstOrDefaultAsync(e => e.Id == id)
                ?? throw new InvalidOperationException("Employee not found.");
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new Employee { TenantId = CurrentTenantId, CreatedBy = user };
            _db.Employees.Add(entity);
        }
        entity.FullName = request.FullName.Trim();
        entity.Email = request.Email;
        entity.Phone = request.Phone;
        entity.Intercom = request.Intercom;
        entity.Designation = request.Designation;
        entity.DepartmentId = request.DepartmentId;
        entity.UserId = request.UserId;
        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(e => e.Department).LoadAsync();
        await _audit.LogAsync(id.HasValue ? "EmployeeUpdated" : "EmployeeCreated", "Employee", entity.Id.ToString(), entity.FullName);
        return new EmployeeDto
        {
            Id = entity.Id,
            FullName = entity.FullName,
            Email = entity.Email,
            Phone = entity.Phone,
            Intercom = entity.Intercom,
            Designation = entity.Designation,
            DepartmentId = entity.DepartmentId,
            DepartmentName = entity.Department.Name,
            UserId = entity.UserId,
            IsActive = entity.IsActive
        };
    }

    public async Task DeactivateEmployeeAsync(Guid id, string? user)
    {
        var entity = await _db.Employees.FindAsync(id) ?? throw new InvalidOperationException("Employee not found.");
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("EmployeeDeactivated", "Employee", id.ToString(), entity.FullName);
    }

    public async Task<IReadOnlyList<MasterItemDto>> GetPurposesAsync(bool activeOnly = true)
    {
        await VisitPurposeDefaults.EnsureOthersPurposeAsync(_db);
        return await GetSimpleAsync(_db.VisitPurposes, activeOnly);
    }

    public async Task<MasterItemDto> UpsertPurposeAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        VisitPurpose entity;
        if (id.HasValue)
        {
            entity = await _db.VisitPurposes.FindAsync(id) ?? throw new InvalidOperationException("Purpose not found.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new VisitPurpose { TenantId = CurrentTenantId, CreatedBy = user }; _db.VisitPurposes.Add(entity); }
        entity.Name = request.Name.Trim(); entity.SortOrder = request.SortOrder; entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, SortOrder = entity.SortOrder };
    }

    public Task<IReadOnlyList<MasterItemDto>> GetLocationsAsync(bool activeOnly = true) =>
        GetLocationsInternal(activeOnly);

    private async Task<IReadOnlyList<MasterItemDto>> GetLocationsInternal(bool activeOnly)
    {
        var q = _db.Locations.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto
        {
            Id = x.Id, Name = x.Name, IsActive = x.IsActive, SortOrder = x.SortOrder,
            RequiresPlantNumber = x.RequiresPlantNumber, RequiresOtherText = x.RequiresOtherText
        }).ToListAsync();
    }

    public async Task<MasterItemDto> UpsertLocationAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        Location entity;
        if (id.HasValue)
        {
            entity = await _db.Locations.FindAsync(id) ?? throw new InvalidOperationException("Location not found.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new Location { TenantId = CurrentTenantId, CreatedBy = user }; _db.Locations.Add(entity); }
        entity.Name = request.Name.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.RequiresPlantNumber = request.RequiresPlantNumber;
        entity.RequiresOtherText = request.RequiresOtherText;
        await _db.SaveChangesAsync();
        return new MasterItemDto
        {
            Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, SortOrder = entity.SortOrder,
            RequiresPlantNumber = entity.RequiresPlantNumber, RequiresOtherText = entity.RequiresOtherText
        };
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
        else { entity = new IdType { CreatedBy = user }; _db.IdTypes.Add(entity); }
        entity.Name = request.Name.Trim(); entity.SortOrder = request.SortOrder; entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, SortOrder = entity.SortOrder };
    }

    public async Task<IReadOnlyList<MasterItemDto>> GetEntryGatesAsync(bool activeOnly = true)
    {
        var q = _db.EntryGates.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.Name).Select(x => new MasterItemDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, IsDefault = x.IsDefault }).ToListAsync();
    }

    public async Task<MasterItemDto> UpsertEntryGateAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        if (request.IsDefault)
        {
            foreach (var g in await _db.EntryGates.Where(x => x.IsDefault).ToListAsync()) g.IsDefault = false;
        }
        EntryGate entity;
        if (id.HasValue)
        {
            entity = await _db.EntryGates.FindAsync(id) ?? throw new InvalidOperationException("Entry gate not found.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new EntryGate { CreatedBy = user }; _db.EntryGates.Add(entity); }
        entity.Name = request.Name.Trim(); entity.IsActive = request.IsActive; entity.IsDefault = request.IsDefault;
        await _db.SaveChangesAsync();
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, IsDefault = entity.IsDefault };
    }

    public async Task<IReadOnlyList<MasterItemDto>> GetExitGatesAsync(bool activeOnly = true)
    {
        var q = _db.ExitGates.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.Name).Select(x => new MasterItemDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, IsDefault = x.IsDefault }).ToListAsync();
    }

    public async Task<MasterItemDto> UpsertExitGateAsync(Guid? id, MasterUpsertRequest request, string? user)
    {
        if (request.IsDefault)
        {
            foreach (var g in await _db.ExitGates.Where(x => x.IsDefault).ToListAsync()) g.IsDefault = false;
        }
        ExitGate entity;
        if (id.HasValue)
        {
            entity = await _db.ExitGates.FindAsync(id) ?? throw new InvalidOperationException("Exit gate not found.");
            entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = user;
        }
        else { entity = new ExitGate { CreatedBy = user }; _db.ExitGates.Add(entity); }
        entity.Name = request.Name.Trim(); entity.IsActive = request.IsActive; entity.IsDefault = request.IsDefault;
        await _db.SaveChangesAsync();
        return new MasterItemDto { Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive, IsDefault = entity.IsDefault };
    }

    public async Task DeactivateMasterAsync(string type, Guid id, string? user)
    {
        switch (type.ToLowerInvariant())
        {
            case "departments":
                var d = await _db.Departments.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                d.IsActive = false; d.UpdatedAt = DateTime.UtcNow; d.UpdatedBy = user; break;
            case "purposes":
                var p = await _db.VisitPurposes.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                p.IsActive = false; p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = user; break;
            case "locations":
                var l = await _db.Locations.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                l.IsActive = false; l.UpdatedAt = DateTime.UtcNow; l.UpdatedBy = user; break;
            case "idtypes":
                var i = await _db.IdTypes.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                i.IsActive = false; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = user; break;
            case "entrygates":
                var eg = await _db.EntryGates.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                eg.IsActive = false; eg.UpdatedAt = DateTime.UtcNow; eg.UpdatedBy = user; break;
            case "exitgates":
                var xg = await _db.ExitGates.FindAsync(id) ?? throw new InvalidOperationException("Not found");
                xg.IsActive = false; xg.UpdatedAt = DateTime.UtcNow; xg.UpdatedBy = user; break;
            default: throw new InvalidOperationException("Unknown master type");
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MasterDeactivated", type, id.ToString(), $"Deactivated {type}");
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
        return await q.OrderBy(x => x.SortOrder).Select(x => new MasterItemDto { Id = x.Id, Name = x.Name, IsActive = x.IsActive, SortOrder = x.SortOrder }).ToListAsync();
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
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public ReportService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<object> GenerateAsync(ReportRequest request, string generatedBy)
    {
        var rows = await QueryRows(request);
        await _audit.LogAsync("ReportGenerated", "Report", request.ReportType, $"{request.ReportType} by {generatedBy}");
        return new
        {
            reportType = request.ReportType,
            generatedAt = DateTime.Now,
            generatedBy,
            total = rows.Count,
            rows
        };
    }

    public async Task<byte[]> ExportExcelAsync(ReportRequest request, string generatedBy)
    {
        var rows = await QueryRows(request);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Visitors");
        ws.Cell(1, 1).Value = "TIAANO Visitor Report";
        ws.Cell(2, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        ws.Cell(3, 1).Value = $"Generated By: {generatedBy}";
        var headers = new[] { "Visit #", "Visitor", "Company", "Host", "Department", "Date", "Status", "Purposes", "Locations" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Value = headers[i];
        var r = 6;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.VisitNumber;
            ws.Cell(r, 2).Value = row.VisitorName;
            ws.Cell(r, 3).Value = row.Company;
            ws.Cell(r, 4).Value = row.Host;
            ws.Cell(r, 5).Value = row.Department;
            ws.Cell(r, 6).Value = row.VisitDate;
            ws.Cell(r, 7).Value = row.Status;
            ws.Cell(r, 8).Value = row.Purposes;
            ws.Cell(r, 9).Value = row.Locations;
            r++;
        }
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        await _audit.LogAsync("ReportExported", "Report", request.ReportType, $"Excel export by {generatedBy}");
        return stream.ToArray();
    }

    public async Task<byte[]> ExportPdfAsync(ReportRequest request, string generatedBy)
    {
        var rows = await QueryRows(request);
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Header().Text("TIAANO Visitor Report").Bold().FontSize(16);
                page.Content().Column(col =>
                {
                    col.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} by {generatedBy}");
                    col.Item().Text($"Report: {request.ReportType} | Records: {rows.Count}").FontSize(10);
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2);
                            c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Visitor").Bold();
                            h.Cell().Text("Company").Bold();
                            h.Cell().Text("Host").Bold();
                            h.Cell().Text("Department").Bold();
                            h.Cell().Text("Date").Bold();
                            h.Cell().Text("Status").Bold();
                        });
                        foreach (var row in rows.Take(200))
                        {
                            table.Cell().Text(row.VisitorName).FontSize(9);
                            table.Cell().Text(row.Company).FontSize(9);
                            table.Cell().Text(row.Host).FontSize(9);
                            table.Cell().Text(row.Department).FontSize(9);
                            table.Cell().Text(row.VisitDate).FontSize(9);
                            table.Cell().Text(row.Status).FontSize(9);
                        }
                    });
                });
            });
        });
        await _audit.LogAsync("ReportExported", "Report", request.ReportType, $"PDF export by {generatedBy}");
        return doc.GeneratePdf();
    }

    public async Task<byte[]> ExportCsvAsync(ReportRequest request, string generatedBy)
    {
        var rows = await QueryRows(request);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# TIAANO Visitor Report generated {DateTime.Now:yyyy-MM-dd HH:mm} by {generatedBy}");
        sb.AppendLine("VisitNumber,Visitor,Company,Host,Department,Date,Status,Purposes,Locations");
        foreach (var row in rows)
        {
            sb.AppendLine($"\"{row.VisitNumber}\",\"{row.VisitorName}\",\"{row.Company}\",\"{row.Host}\",\"{row.Department}\",\"{row.VisitDate}\",\"{row.Status}\",\"{row.Purposes}\",\"{row.Locations}\"");
        }
        await _audit.LogAsync("ReportExported", "Report", request.ReportType, $"CSV export by {generatedBy}");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private async Task<List<ReportRow>> QueryRows(ReportRequest request)
    {
        var from = request.DateFrom ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var to = request.DateTo ?? DateOnly.FromDateTime(DateTime.Today);
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
        }

        var list = await q.OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.VisitTime).Take(5000).ToListAsync();
        return list.Select(v => new ReportRow(
            v.VisitNumber,
            v.Visitor.FullName,
            v.Visitor.CompanyName,
            v.HostEmployee.FullName,
            v.Department.Name,
            v.VisitDate.ToString("yyyy-MM-dd"),
            v.Status.ToString(),
            string.Join("; ", v.VisitPurposes.Select(p => p.VisitPurpose.Name)),
            string.Join("; ", v.VisitLocations.Select(l => l.Location.Name))
        )).ToList();
    }

    private record ReportRow(string VisitNumber, string VisitorName, string Company, string Host, string Department, string VisitDate, string Status, string Purposes, string Locations);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> GetUsersAsync();
    Task<UserDto> CreateAsync(CreateUserRequest request, string? actor);
    Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string? actor);
}

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenant;

    public UserAdminService(UserManager<ApplicationUser> userManager, ApplicationDbContext db, IAuditService audit, ITenantContext tenant)
    {
        _userManager = userManager;
        _db = db;
        _audit = audit;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync()
    {
        var users = await _userManager.Users.Include(u => u.Department).OrderBy(u => u.FullName).ToListAsync();
        var result = new List<UserDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(new UserDto
            {
                Id = u.Id,
                Username = u.UserName ?? "",
                FullName = u.FullName,
                Email = u.Email ?? "",
                Roles = roles.ToList(),
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department?.Name,
                MustChangePassword = u.MustChangePassword,
                IsActive = u.IsActive
            });
        }
        return result;
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, string? actor)
    {
        if (!AppRoles.All.Contains(request.Role))
            throw new InvalidOperationException("Invalid role.");
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            TenantId = _tenant.TenantId ?? WellKnownTenants.TiaanoId,
            DepartmentId = request.DepartmentId,
            IsActive = true,
            MustChangePassword = true,
            EmailConfirmed = true,
            CreatedBy = actor
        };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await _userManager.AddToRoleAsync(user, request.Role);
        await _audit.LogAsync("UserCreated", "User", user.Id, $"Created {user.UserName}");
        return (await GetUsersAsync()).First(u => u.Id == user.Id);
    }

    public async Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string? actor)
    {
        var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new InvalidOperationException("User not found.");
        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim();
        user.DepartmentId = request.DepartmentId;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = actor;
        await _userManager.UpdateAsync(user);
        var roles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, roles);
        await _userManager.AddToRoleAsync(user, request.Role);
        await _audit.LogAsync("UserUpdated", "User", user.Id, $"Updated {user.UserName}");
        return (await GetUsersAsync()).First(u => u.Id == user.Id);
    }
}
