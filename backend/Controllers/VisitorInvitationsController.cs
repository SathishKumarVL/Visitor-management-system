using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

[ApiController]
[Route("visitors/invitations")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Reception},{AppRoles.Host}")]
[RequireModule(ModuleKeys.VisitorManagement)]
public class VisitorInvitationsController : ControllerBase
{
    private readonly IVisitorInvitationService _invitations;

    public VisitorInvitationsController(IVisitorInvitationService invitations) => _invitations = invitations;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VisitorInvitationDto>>>> List() =>
        Ok(new ApiResponse<IReadOnlyList<VisitorInvitationDto>>(true, await _invitations.ListAsync(User)));

    [HttpPost]
    public Task<ActionResult<ApiResponse<VisitorInvitationDto>>> Create([FromBody] CreateVisitorInvitationRequest request) =>
        Send(() => _invitations.CreateAsync(request, User, Request.Headers.Origin.ToString()));

    [HttpPost("{id:guid}/resend")]
    public Task<ActionResult<ApiResponse<VisitorInvitationDto>>> Resend(Guid id) =>
        Send(() => _invitations.ResendAsync(id, User, Request.Headers.Origin.ToString()));

    [HttpPost("{id:guid}/withdraw")]
    public async Task<ActionResult<ApiResponse<object>>> Withdraw(Guid id)
    {
        try
        {
            await _invitations.WithdrawAsync(id, User);
            return Ok(new ApiResponse<object>(true, null, "Invitation withdrawn. The link no longer works."));
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

    private async Task<ActionResult<ApiResponse<VisitorInvitationDto>>> Send(
        Func<Task<(VisitorInvitationDto Invitation, string? EmailProblem)>> send)
    {
        try
        {
            var (invitation, problem) = await send();
            var message = problem ?? $"Invitation sent to {invitation.Email}. The link works once and expires in 24 hours.";
            return Ok(new ApiResponse<VisitorInvitationDto>(true, invitation, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ApiResponse<VisitorInvitationDto>(false, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<VisitorInvitationDto>(false, null, ex.Message));
        }
    }
}

/// <summary>The visitor's side of an invitation: no sign-in, so the link token is the only credential.</summary>
[ApiController]
[Route("public/invitations")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy.PublicInvitePolicyName)]
public class PublicInvitationsController : ControllerBase
{
    private readonly IVisitorInvitationService _invitations;

    public PublicInvitationsController(IVisitorInvitationService invitations) => _invitations = invitations;

    /// <summary>The token travels in the body, not the URL, so it stays out of server and proxy logs.</summary>
    [HttpPost("open")]
    public async Task<ActionResult<ApiResponse<PublicInvitationDto>>> Open([FromBody] PublicInvitationTokenRequest request)
    {
        try
        {
            return Ok(new ApiResponse<PublicInvitationDto>(true, await _invitations.GetPublicAsync(request.Token)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<PublicInvitationDto>(false, null, ex.Message));
        }
    }

    [HttpPost("submit")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ApiResponse<PublicInvitationResultDto>>> Submit([FromBody] PublicInvitationSubmitRequest request)
    {
        try
        {
            var result = await _invitations.SubmitAsync(request.Token, request);
            return Ok(new ApiResponse<PublicInvitationResultDto>(true, result, "Your appointment is booked."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<PublicInvitationResultDto>(false, null, ex.Message));
        }
    }
}
