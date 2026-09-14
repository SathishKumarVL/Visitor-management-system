using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Models.Product;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    public const string AppVersion = "0.2.0-overnight";

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;

    public SystemController(ApplicationDbContext db, ITenantContext tenant, IWebHostEnvironment env)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Health()
    {
        var dbOk = false;
        try
        {
            dbOk = await _db.Database.CanConnectAsync();
        }
        catch
        {
            dbOk = false;
        }

        var payload = new
        {
            status = dbOk ? "Healthy" : "Degraded",
            version = AppVersion,
            environment = _env.EnvironmentName,
            database = dbOk,
            utc = DateTime.UtcNow
        };
        return Ok(new ApiResponse<object>(true, payload));
    }

    [HttpGet("version")]
    [Authorize]
    public ActionResult<ApiResponse<object>> Version() =>
        Ok(new ApiResponse<object>(true, new
        {
            application = AppVersion,
            api = "v1",
            product = "TIAANO Visitor Management Platform"
        }));

    [HttpGet("license")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<ApiResponse<object>>> License()
    {
        var tenantId = _tenant.TenantId ?? WellKnownTenants.TiaanoId;
        var license = await _db.Set<TenantLicense>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);
        var modules = await _db.Set<TenantModuleEntitlement>().AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new { m.ModuleKey, m.IsEnabled, m.ExpiresAt })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, new
        {
            tenantId,
            edition = license?.Edition ?? "Starter",
            status = license?.Status ?? "Active",
            expiresAt = license?.ExpiresAt,
            graceDaysAfterExpiry = license?.GraceDaysAfterExpiry ?? 30,
            maxSites = license?.MaxSites ?? 1,
            maxUsers = license?.MaxUsers ?? 25,
            modules
        }));
    }
}
