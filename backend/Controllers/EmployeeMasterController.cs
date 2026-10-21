using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

/// <summary>
/// Employee menu: employee profiles with their face. Admins add employees at their own location; only Super
/// Admins edit or deactivate them.
/// </summary>
[ApiController]
[Route("employee-master")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
public class EmployeeMasterController : ControllerBase
{
    private readonly IEmployeeMasterService _employees;

    public EmployeeMasterController(IEmployeeMasterService employees) => _employees = employees;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeDto>>>> List(
        [FromQuery] Guid? locationId,
        [FromQuery] Guid? departmentId,
        [FromQuery] bool activeOnly = false) =>
        Ok(new ApiResponse<IReadOnlyList<EmployeeDto>>(true, await _employees.ListAsync(locationId, departmentId, activeOnly)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Create([FromBody] EmployeeProfileRequest request) =>
        Ok(new ApiResponse<EmployeeDto>(true, await _employees.UpsertAsync(null, request, User.Identity?.Name), "Employee added."));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Update(Guid id, [FromBody] EmployeeProfileRequest request) =>
        Ok(new ApiResponse<EmployeeDto>(true, await _employees.UpsertAsync(id, request, User.Identity?.Name), "Employee updated."));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(Guid id)
    {
        await _employees.DeactivateAsync(id, User.Identity?.Name);
        return Ok(new ApiResponse<object>(true, null, "Deactivated."));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        await _employees.DeleteAsync(id);
        return Ok(new ApiResponse<object>(true, null, "Deleted."));
    }
}
