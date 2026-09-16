using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (result is null)
            return Unauthorized(new ApiResponse<LoginResponse>(false, null, "Invalid username or password."));
        return Ok(new ApiResponse<LoginResponse>(true, result));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await _auth.RefreshAsync(request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (result is null)
            return Unauthorized(new ApiResponse<LoginResponse>(false, null, "Session expired. Please sign in again."));
        return Ok(new ApiResponse<LoginResponse>(true, result));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            await _auth.ChangePasswordAsync(User, request);
            return Ok(new ApiResponse<object>(true, null, "Password updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>(false, null, ex.Message));
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me()
    {
        var user = await _auth.GetCurrentUserAsync(User);
        if (user is null) return Unauthorized(new ApiResponse<UserDto>(false, null, "Session expired."));
        return Ok(new ApiResponse<UserDto>(true, user));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Logout([FromBody] RefreshTokenRequest? request)
    {
        await _auth.LogoutAsync(User, request?.RefreshToken);
        return Ok(new ApiResponse<object>(true, null, "Logged out."));
    }
}

[ApiController]
[Route("api/visitors")]
[Authorize]
[RequireModule(ModuleKeys.VisitorManagement)]
public class VisitorsController : ControllerBase
{
    private readonly IVisitorService _visitors;
    private readonly ISettingsService _settings;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public VisitorsController(IVisitorService visitors, ISettingsService settings, IWebHostEnvironment env, IConfiguration config)
    {
        _visitors = visitors;
        _settings = settings;
        _env = env;
        _config = config;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<VisitorListItemDto>>>> Search([FromQuery] VisitorSearchRequest request)
    {
        var settings = await _settings.GetAsync();
        var result = await _visitors.SearchAsync(request, User, settings.MaxVisitDurationWarningMinutes);
        return Ok(new ApiResponse<PagedResult<VisitorListItemDto>>(true, result));
    }

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception}")]
    public async Task<ActionResult<ApiResponse<VisitorDetailDto>>> Register([FromBody] RegisterVisitorRequest request)
    {
        try
        {
            var result = await _visitors.RegisterAsync(request, User, _env.WebRootPath);
            return Ok(new ApiResponse<VisitorDetailDto>(true, result, "Visitor registered successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorDetailDto>(false, null, ex.Message));
        }
    }

    [HttpPost("expected")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Host}")]
    public async Task<ActionResult<ApiResponse<VisitorDetailDto>>> Expected([FromBody] ExpectedVisitorRequest request)
    {
        try
        {
            var result = await _visitors.CreateExpectedAsync(request, User);
            return Ok(new ApiResponse<VisitorDetailDto>(true, result, "Expected visitor created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorDetailDto>(false, null, ex.Message));
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VisitorDetailDto>>> Get(Guid id)
    {
        var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
        var result = await _visitors.GetAsync(id, User, key);
        if (result is null) return NotFound(new ApiResponse<VisitorDetailDto>(false, null, "Visitor not found."));
        return Ok(new ApiResponse<VisitorDetailDto>(true, result));
    }

    /// <summary>Recognises a returning visitor from a live face capture. Scoped to the caller's tenant.</summary>
    [HttpPost("face-search")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception}")]
    public async Task<ActionResult<ApiResponse<FaceSearchMatchDto>>> FaceSearch([FromBody] FaceSearchRequest request)
    {
        try
        {
            var match = await _visitors.FaceSearchAsync(request.PhotoBase64, User);
            return Ok(new ApiResponse<FaceSearchMatchDto>(true, match,
                match is null ? "No matching visitor found." : "Returning visitor recognised."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<FaceSearchMatchDto>(false, null, ex.Message));
        }
    }

    /// <summary>Recognises a visitor who is currently inside so they can be checked out by face.</summary>
    [HttpPost("face-identify-inside")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
    public async Task<ActionResult<ApiResponse<FaceCheckoutMatchDto>>> FaceIdentifyInside(
        [FromBody] FaceSearchRequest request)
    {
        try
        {
            var match = await _visitors.FaceIdentifyInsideAsync(request.PhotoBase64, User);
            return Ok(new ApiResponse<FaceCheckoutMatchDto>(true, match,
                match is null ? "No matching visitor is currently inside." : "Visitor recognised."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<FaceCheckoutMatchDto>(false, null, ex.Message));
        }
    }

    [HttpGet("inside")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VisitorListItemDto>>>> Inside()
    {
        var settings = await _settings.GetAsync();
        return Ok(new ApiResponse<IReadOnlyList<VisitorListItemDto>>(true, await _visitors.GetInsideAsync(settings.MaxVisitDurationWarningMinutes)));
    }

    [HttpGet("expected")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VisitorListItemDto>>>> ExpectedList([FromQuery] DateOnly? date)
    {
        return Ok(new ApiResponse<IReadOnlyList<VisitorListItemDto>>(true, await _visitors.GetExpectedAsync(date)));
    }

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
    public async Task<ActionResult<ApiResponse<PassDto>>> CheckIn(Guid id, [FromBody] CheckInRequest? request)
    {
        try
        {
            var result = await _visitors.CheckInAsync(id, request ?? new CheckInRequest(), User, _env.WebRootPath);
            return Ok(new ApiResponse<PassDto>(true, result, "Visitor checked in."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<PassDto>(false, null, ex.Message));
        }
    }

    [HttpPost("{id:guid}/check-out")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
    public async Task<ActionResult<ApiResponse<VisitorListItemDto>>> CheckOut(Guid id, [FromBody] CheckOutRequest? request)
    {
        try
        {
            var result = await _visitors.CheckOutAsync(id, request ?? new CheckOutRequest(), User);
            return Ok(new ApiResponse<VisitorListItemDto>(true, result, "Visitor checked out."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorListItemDto>(false, null, ex.Message));
        }
    }
}

[ApiController]
[Route("api/approvals")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Host}")]
[RequireModule(ModuleKeys.VisitorManagement)]
public class ApprovalsController : ControllerBase
{
    private readonly IVisitorService _visitors;

    public ApprovalsController(IVisitorService visitors) => _visitors = visitors;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VisitorListItemDto>>>> List() =>
        Ok(new ApiResponse<IReadOnlyList<VisitorListItemDto>>(true, await _visitors.GetPendingApprovalsAsync(User)));

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<ApiResponse<VisitorListItemDto>>> Approve(Guid id)
    {
        try
        {
            return Ok(new ApiResponse<VisitorListItemDto>(true, await _visitors.ApproveAsync(id, User), "Approved."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<VisitorListItemDto>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorListItemDto>(false, null, ex.Message));
        }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<VisitorListItemDto>>> Reject(Guid id, [FromBody] ApprovalActionRequest request)
    {
        try
        {
            return Ok(new ApiResponse<VisitorListItemDto>(true, await _visitors.RejectAsync(id, request.Reason ?? "", User), "Rejected."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<VisitorListItemDto>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorListItemDto>(false, null, ex.Message));
        }
    }
}

[ApiController]
[Route("api/pass")]
[Authorize]
[RequireModule(ModuleKeys.VisitorManagement)]
public class PassController : ControllerBase
{
    private readonly IVisitorService _visitors;
    private readonly IWebHostEnvironment _env;

    public PassController(IVisitorService visitors, IWebHostEnvironment env)
    {
        _visitors = visitors;
        _env = env;
    }

    [HttpPost("{visitId:guid}/generate")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
    public async Task<ActionResult<ApiResponse<PassDto>>> Generate(Guid visitId)
    {
        try
        {
            var result = await _visitors.ReprintPassAsync(visitId, User, _env.WebRootPath);
            return Ok(new ApiResponse<PassDto>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<PassDto>(false, null, ex.Message));
        }
    }

    [HttpGet("{visitId:guid}")]
    public async Task<ActionResult<ApiResponse<PassDto>>> Get(Guid visitId)
    {
        var result = await _visitors.GetPassAsync(visitId, _env.WebRootPath);
        if (result is null) return NotFound(new ApiResponse<PassDto>(false, null, "Pass not found."));
        return Ok(new ApiResponse<PassDto>(true, result));
    }

    [HttpGet("verify/{visitNumber}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
    public async Task<ActionResult<ApiResponse<PassDto>>> Verify(string visitNumber)
    {
        var result = await _visitors.LookupByVisitNumberAsync(visitNumber, _env.WebRootPath);
        if (result is null)
            return NotFound(new ApiResponse<PassDto>(false, null, "Visitor pass not found."));
        return Ok(new ApiResponse<PassDto>(true, result));
    }
}

[ApiController]
[Route("api/dashboard")]
[Authorize]
[RequireModule(ModuleKeys.VisitorManagement)]
public class DashboardController : ControllerBase
{
    private readonly IVisitorService _visitors;
    private readonly ISettingsService _settings;

    public DashboardController(IVisitorService visitors, ISettingsService settings)
    {
        _visitors = visitors;
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Get()
    {
        var settings = await _settings.GetAsync();
        return Ok(new ApiResponse<DashboardDto>(true, await _visitors.GetDashboardAsync(User, settings.MaxVisitDurationWarningMinutes)));
    }
}

[ApiController]
[Route("api/masters")]
[Authorize]
public class MastersController : ControllerBase
{
    private readonly IMasterDataService _masters;

    public MastersController(IMasterDataService masters) => _masters = masters;

    [HttpGet("departments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> Departments([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetDepartmentsAsync(activeOnly)));

    [HttpPost("departments")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreateDepartment([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertDepartmentAsync(null, request, User.Identity?.Name)));

    [HttpPut("departments/{id:guid}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> UpdateDepartment(Guid id, [FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertDepartmentAsync(id, request, User.Identity?.Name)));

    [HttpGet("hosts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeDto>>>> Hosts([FromQuery] Guid? departmentId, [FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<EmployeeDto>>(true, await _masters.GetHostsAsync(departmentId, activeOnly)));

    [HttpPost("hosts")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> CreateHost([FromBody] CreateEmployeeRequest request) =>
        Ok(new ApiResponse<EmployeeDto>(true, await _masters.UpsertEmployeeAsync(null, request, User.Identity?.Name)));

    [HttpPut("hosts/{id:guid}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> UpdateHost(Guid id, [FromBody] CreateEmployeeRequest request) =>
        Ok(new ApiResponse<EmployeeDto>(true, await _masters.UpsertEmployeeAsync(id, request, User.Identity?.Name)));

    [HttpPost("hosts/{id:guid}/deactivate")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<object>>> DeactivateHost(Guid id)
    {
        await _masters.DeactivateEmployeeAsync(id, User.Identity?.Name);
        return Ok(new ApiResponse<object>(true, null, "Deactivated."));
    }

    [HttpGet("purposes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> Purposes([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetPurposesAsync(activeOnly)));

    [HttpPost("purposes")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreatePurpose([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertPurposeAsync(null, request, User.Identity?.Name)));

    [HttpPut("purposes/{id:guid}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> UpdatePurpose(Guid id, [FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertPurposeAsync(id, request, User.Identity?.Name)));

    [HttpGet("locations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> Locations([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetLocationsAsync(activeOnly)));

    [HttpPost("locations")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreateLocation([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertLocationAsync(null, request, User.Identity?.Name)));

    [HttpPut("locations/{id:guid}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> UpdateLocation(Guid id, [FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertLocationAsync(id, request, User.Identity?.Name)));

    [HttpGet("id-types")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> IdTypes([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetIdTypesAsync(activeOnly)));

    [HttpPost("id-types")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreateIdType([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertIdTypeAsync(null, request, User.Identity?.Name)));

    [HttpGet("entry-gates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> EntryGates([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetEntryGatesAsync(activeOnly)));

    [HttpPost("entry-gates")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreateEntryGate([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertEntryGateAsync(null, request, User.Identity?.Name)));

    [HttpGet("exit-gates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterItemDto>>>> ExitGates([FromQuery] bool activeOnly = true) =>
        Ok(new ApiResponse<IReadOnlyList<MasterItemDto>>(true, await _masters.GetExitGatesAsync(activeOnly)));

    [HttpPost("exit-gates")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<MasterItemDto>>> CreateExitGate([FromBody] MasterUpsertRequest request) =>
        Ok(new ApiResponse<MasterItemDto>(true, await _masters.UpsertExitGateAsync(null, request, User.Identity?.Name)));

    [HttpPost("{type}/{id:guid}/deactivate")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(string type, Guid id)
    {
        await _masters.DeactivateMasterAsync(type, id, User.Identity?.Name);
        return Ok(new ApiResponse<object>(true, null, "Deactivated."));
    }
}

[ApiController]
[Route("api/users")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
public class UsersController : ControllerBase
{
    private readonly IUserAdminService _users;
    public UsersController(IUserAdminService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDto>>>> Get() =>
        Ok(new ApiResponse<IReadOnlyList<UserDto>>(true, await _users.GetUsersAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            return Ok(new ApiResponse<UserDto>(true, await _users.CreateAsync(request, User.Identity?.Name)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<UserDto>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<UserDto>(false, null, ex.Message));
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(string id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            return Ok(new ApiResponse<UserDto>(true, await _users.UpdateAsync(id, request, User.Identity?.Name)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<UserDto>(false, null, ex.Message));
        }
    }
}

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;

    public SettingsController(ISettingsService settings, INotificationService notifications)
    {
        _settings = settings;
        _notifications = notifications;
    }

    /// <summary>Public branding only — no operational/security settings.</summary>
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PublicBrandingDto>>> GetBranding()
    {
        var s = await _settings.GetPublicBrandingAsync();
        return Ok(new ApiResponse<PublicBrandingDto>(true, new PublicBrandingDto
        {
            CompanyName = s.CompanyName,
            LogoPath = s.LogoPath
        }));
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> Get() =>
        Ok(new ApiResponse<SettingsDto>(true, await _settings.GetAsync()));

    [HttpPut]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> Update([FromBody] SettingsDto dto) =>
        Ok(new ApiResponse<SettingsDto>(true, await _settings.UpdateAsync(dto, User.Identity?.Name)));

    [HttpPost("test-email")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception}")]
    public async Task<ActionResult<ApiResponse<object>>> TestEmail([FromBody] TestEmailRequest request)
    {
        var (ok, message) = await _notifications.SendTestEmailAsync(request.To);
        if (!ok) return BadRequest(new ApiResponse<object>(false, null, message));
        return Ok(new ApiResponse<object>(true, null, message));
    }
}

public class TestEmailRequest
{
    public string To { get; set; } = string.Empty;
}

[ApiController]
[Route("api/reports")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception}")]
[RequireModule(ModuleKeys.VisitorManagement)]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet("visitors")]
    public async Task<ActionResult> Get([FromQuery] ReportRequest request)
    {
        var userName = User.Identity?.Name ?? "unknown";
        if (string.Equals(request.Format, "excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await _reports.ExportExcelAsync(request, userName);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"tiaano-visitors-{DateTime.Now:yyyyMMddHHmm}.xlsx");
        }
        if (string.Equals(request.Format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await _reports.ExportPdfAsync(request, userName);
            return File(bytes, "application/pdf", $"tiaano-visitors-{DateTime.Now:yyyyMMddHHmm}.pdf");
        }
        if (string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await _reports.ExportCsvAsync(request, userName);
            return File(bytes, "text/csv", $"tiaano-visitors-{DateTime.Now:yyyyMMddHHmm}.csv");
        }
        return Ok(new ApiResponse<object>(true, await _reports.GenerateAsync(request, userName)));
    }
}

[ApiController]
[Route("api/audit")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
public class AuditController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AuditController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? action = null)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.Contains(action));
        var total = await q.CountAsync();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var items = await q.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                Action = a.Action,
                Entity = a.Entity,
                EntityId = a.EntityId,
                UserName = a.UserName,
                Description = a.Description,
                IpAddress = a.IpAddress,
                CreatedAt = a.CreatedAt
            }).ToListAsync();
        return Ok(new ApiResponse<PagedResult<AuditLogDto>>(true, new PagedResult<AuditLogDto>(items, total, page, pageSize)));
    }
}
