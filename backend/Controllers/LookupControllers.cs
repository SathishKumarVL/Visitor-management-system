using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

/// <summary>External APIs that purpose fields can search. Only SuperAdmin sees URLs and sets keys.</summary>
[ApiController]
[Route("settings/lookup-sources")]
[Authorize(Roles = AppRoles.SuperAdmin)]
[RequireModule(ModuleKeys.VisitorManagement)]
public class LookupSourcesController : ControllerBase
{
    private readonly ILookupService _lookups;
    public LookupSourcesController(ILookupService lookups) => _lookups = lookups;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupSourceDto>>>> List() =>
        Ok(new ApiResponse<IReadOnlyList<LookupSourceDto>>(true, await _lookups.ListAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<LookupSourceDto>>> Create([FromBody] LookupSourceUpsertRequest request) =>
        Ok(new ApiResponse<LookupSourceDto>(true, await _lookups.UpsertAsync(null, request, User.Identity?.Name)));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<LookupSourceDto>>> Update(Guid id, [FromBody] LookupSourceUpsertRequest request) =>
        Ok(new ApiResponse<LookupSourceDto>(true, await _lookups.UpsertAsync(id, request, User.Identity?.Name)));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        await _lookups.DeleteAsync(id);
        return Ok(new ApiResponse<object>(true, null));
    }

    [HttpPost("test")]
    public async Task<ActionResult<ApiResponse<LookupTestResultDto>>> Test([FromBody] LookupTestRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<LookupTestResultDto>(true, await _lookups.TestAsync(request, ct)));
}

/// <summary>Used while registering a visitor: the browser never calls the external API directly.</summary>
[ApiController]
[Route("lookups")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception}")]
[RequireModule(ModuleKeys.VisitorManagement)]
public class LookupsController : ControllerBase
{
    private readonly ILookupService _lookups;
    public LookupsController(ILookupService lookups) => _lookups = lookups;

    [HttpGet("sources")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupSourceOptionDto>>>> Sources() =>
        Ok(new ApiResponse<IReadOnlyList<LookupSourceOptionDto>>(true, await _lookups.OptionsAsync()));

    [HttpGet("{sourceId:guid}/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupMatchDto>>>> Search(Guid sourceId, [FromQuery] string? q, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<LookupMatchDto>>(true, await _lookups.SearchAsync(sourceId, q, null, ct)));

    /// <summary>Same as the GET, plus the purpose's other field values for sources whose request uses them.</summary>
    [HttpPost("{sourceId:guid}/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupMatchDto>>>> SearchWithContext(
        Guid sourceId, [FromBody] LookupSearchRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<LookupMatchDto>>(true, await _lookups.SearchAsync(sourceId, request.Query, request.Context, ct)));

    /// <summary>Confirms a returning visitor's earlier pick is still on record and returns it as a fresh pick.</summary>
    [HttpPost("{sourceId:guid}/recheck")]
    public async Task<ActionResult<ApiResponse<LookupMatchDto?>>> Recheck(
        Guid sourceId, [FromBody] LookupRecheckRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<LookupMatchDto?>(true, await _lookups.RecheckAsync(sourceId, request, ct)));

    [HttpPost("check")]
    public ActionResult<ApiResponse<LookupCheckResultDto>> Check([FromBody] LookupCheckRequest request) =>
        Ok(new ApiResponse<LookupCheckResultDto>(true, _lookups.Check(request)));
}
