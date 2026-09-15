using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

/// <summary>Emergency roster — requires emergency-management entitlement (backend enforced).</summary>
[ApiController]
[Route("api/emergency")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Security}")]
[RequireModule(ModuleKeys.EmergencyManagement)]
public class EmergencyController : ControllerBase
{
    private readonly IVisitorService _visitors;
    private readonly ISettingsService _settings;

    public EmergencyController(IVisitorService visitors, ISettingsService settings)
    {
        _visitors = visitors;
        _settings = settings;
    }

    [HttpGet("inside")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VisitorListItemDto>>>> Inside()
    {
        var settings = await _settings.GetAsync();
        return Ok(new ApiResponse<IReadOnlyList<VisitorListItemDto>>(
            true, await _visitors.GetInsideAsync(settings.MaxVisitDurationWarningMinutes)));
    }
}

/// <summary>Analytics — requires analytics entitlement (backend enforced).</summary>
[ApiController]
[Route("api/analytics")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
[RequireModule(ModuleKeys.Analytics)]
public class AnalyticsController : ControllerBase
{
    private readonly IVisitorService _visitors;
    private readonly ISettingsService _settings;

    public AnalyticsController(IVisitorService visitors, ISettingsService settings)
    {
        _visitors = visitors;
        _settings = settings;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<object>>> Summary()
    {
        var settings = await _settings.GetAsync();
        var dash = await _visitors.GetDashboardAsync(User, settings.MaxVisitDurationWarningMinutes);
        return Ok(new ApiResponse<object>(true, new
        {
            dash.VisitorsToday,
            dash.CurrentlyInside,
            dash.PendingApprovals,
            dash.ExpectedToday
        }));
    }
}
