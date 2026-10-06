using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

/// <summary>
/// Endpoints for other applications. Authenticated only by an <c>X-Api-Key</c> header created under
/// Settings -> API keys; each key is limited to the scopes it was given.
/// </summary>
[ApiController]
[Route("integration")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[RequireModule(ModuleKeys.VisitorManagement)]
public class IntegrationController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly IMasterImportService _import;
    private readonly ApplicationDbContext _db;

    public IntegrationController(IReportService reports, IMasterImportService import, ApplicationDbContext db)
    {
        _reports = reports;
        _import = import;
        _db = db;
    }

    /// <summary>
    /// Same data as Reports in the app. Optional <c>location</c> accepts a location name or code
    /// (alternative to <c>locationId</c>). <c>format</c>: json (default), csv or excel.
    /// </summary>
    [HttpGet("reports/visitors")]
    [Authorize(Policy = ApiKeyDefaults.ReportsPolicy)]
    public async Task<ActionResult> Reports([FromQuery] ReportRequest request, [FromQuery] string? location)
    {
        if (!string.IsNullOrWhiteSpace(location) && request.LocationId is null)
        {
            var value = location.Trim();
            var loc = await _db.Locations.AsNoTracking()
                .Where(l => l.Name == value || l.Code == value)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync();
            if (loc is null)
                return BadRequest(new ApiResponse<object>(false, null, $"Location '{value}' was not found."));
            request.LocationId = loc;
        }

        var generatedBy = User.Identity?.Name ?? "api";
        var stamp = AppTime.Now.ToString("yyyyMMddHHmm");
        if (string.Equals(request.Format, "excel", StringComparison.OrdinalIgnoreCase))
            return File(await _reports.ExportExcelAsync(request, generatedBy),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"tiaano-visitors-{stamp}.xlsx");
        if (string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
            return File(await _reports.ExportCsvAsync(request, generatedBy), "text/csv", $"tiaano-visitors-{stamp}.csv");

        return Ok(new ApiResponse<object>(true, await _reports.GenerateAsync(request, generatedBy)));
    }

    /// <summary>Locations and departments with their codes, for mapping in the other application.</summary>
    [HttpGet("masters")]
    public async Task<ActionResult> Masters()
    {
        var locations = await _db.Locations.AsNoTracking()
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .Select(l => new IntegrationLookupDto { Id = l.Id, Name = l.Name, Code = l.Code, Active = l.IsActive })
            .ToListAsync();
        var departments = await _db.Departments.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new IntegrationLookupDto { Id = d.Id, Name = d.Name, Code = d.Code, Active = d.IsActive })
            .ToListAsync();
        return Ok(new ApiResponse<object>(true, new { locations, departments }));
    }

    [HttpGet("employees")]
    [Authorize(Policy = ApiKeyDefaults.EmployeesPolicy)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IntegrationEmployeeDto>>>> Employees([FromQuery] bool includeInactive = false)
    {
        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(e => e.IsActive);
        var rows = await q.OrderBy(e => e.FullName)
            .Select(e => new IntegrationEmployeeDto
            {
                Id = e.Id,
                Code = e.Code,
                FullName = e.FullName,
                Designation = e.Designation,
                Department = e.Department.Name,
                DepartmentCode = e.Department.Code,
                Location = e.Location != null ? e.Location.Name : null,
                LocationCode = e.Location != null ? e.Location.Code : null,
                Phone = e.Phone,
                Email = e.Email,
                Intercom = e.Intercom,
                Active = e.IsActive,
            })
            .ToListAsync();
        return Ok(new ApiResponse<IReadOnlyList<IntegrationEmployeeDto>>(true, rows));
    }

    /// <summary>
    /// Creates or updates employees, matched on <c>code</c>. Each employee succeeds or fails on its own;
    /// failures are listed with their position in the array.
    /// </summary>
    [HttpPost("employees")]
    [Authorize(Policy = ApiKeyDefaults.EmployeesPolicy)]
    public async Task<ActionResult<ApiResponse<IntegrationEmployeeBatchResult>>> PushEmployees(
        [FromBody] IntegrationEmployeeBatchRequest request)
    {
        try
        {
            var result = await _import.ImportEmployeesAsync(request.Employees, User.Identity?.Name);
            var message = result.Failed == 0 ? null : $"{result.Failed} of {result.Total} employee(s) could not be saved.";
            return Ok(new ApiResponse<IntegrationEmployeeBatchResult>(result.Failed == 0, result, message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<IntegrationEmployeeBatchResult>(false, null, ex.Message));
        }
    }
}

[ApiController]
[Route("settings/api-keys")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class IntegrationKeysController : ControllerBase
{
    private readonly IIntegrationKeyService _keys;
    public IntegrationKeysController(IIntegrationKeyService keys) => _keys = keys;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IntegrationApiKeyDto>>>> List() =>
        Ok(new ApiResponse<IReadOnlyList<IntegrationApiKeyDto>>(true, await _keys.ListAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CreatedIntegrationApiKeyDto>>> Create([FromBody] CreateIntegrationApiKeyRequest request)
    {
        try
        {
            return Ok(new ApiResponse<CreatedIntegrationApiKeyDto>(true, await _keys.CreateAsync(request, User.Identity?.Name)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<CreatedIntegrationApiKeyDto>(false, null, ex.Message));
        }
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<ActionResult<ApiResponse<object>>> Revoke(Guid id)
    {
        try
        {
            await _keys.RevokeAsync(id, User.Identity?.Name);
            return Ok(new ApiResponse<object>(true, null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>(false, null, ex.Message));
        }
    }
}
