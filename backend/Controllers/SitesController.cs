using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
    private readonly ISiteService _sites;
    public SitesController(ISiteService sites) => _sites = sites;

    /// <summary>
    /// Readable by any authenticated user so reception and security screens can label the active site.
    /// A site-bound user still only ever sees rows their tenant owns.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SiteDto>>>> Get([FromQuery] bool activeOnly = false) =>
        Ok(new ApiResponse<IReadOnlyList<SiteDto>>(true, await _sites.GetAsync(activeOnly)));

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<SiteDto>>> Create([FromBody] SiteUpsertRequest request) =>
        await Run(() => _sites.UpsertAsync(null, request, User.Identity?.Name));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<SiteDto>>> Update(Guid id, [FromBody] SiteUpsertRequest request) =>
        await Run(() => _sites.UpsertAsync(id, request, User.Identity?.Name));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(Guid id)
    {
        try
        {
            await _sites.DeactivateAsync(id, User.Identity?.Name);
            return Ok(new ApiResponse<object>(true, null, "Site deactivated."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<object>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>(false, null, ex.Message));
        }
    }

    private async Task<ActionResult<ApiResponse<SiteDto>>> Run(Func<Task<SiteDto>> action)
    {
        try
        {
            return Ok(new ApiResponse<SiteDto>(true, await action()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<SiteDto>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<SiteDto>(false, null, ex.Message));
        }
    }
}
