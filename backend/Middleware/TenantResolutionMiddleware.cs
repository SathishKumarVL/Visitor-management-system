using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Middleware;

/// <summary>
/// Binds tenant context from authenticated user claims. Client headers are ignored.
/// Missing/inactive tenant fails closed (401) — never falls back to a default tenant for auth'd requests.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ApplicationDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var raw = context.User.FindFirstValue(TenantClaims.TenantIdClaim);
            if (!Guid.TryParse(raw, out var tenantId) || tenantId == Guid.Empty)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { success = false, message = "Tenant context is required." });
                return;
            }

            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant is null || !tenant.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { success = false, message = "Tenant is inactive or unknown." });
                return;
            }

            Guid? locationId = null;
            var locationRaw = context.User.FindFirstValue(TenantClaims.LocationIdClaim);
            if (Guid.TryParse(locationRaw, out var lid) && lid != Guid.Empty)
            {
                var locationOk = await db.Locations.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(l => l.Id == lid && l.TenantId == tenantId && l.IsActive);
                if (locationOk) locationId = lid;
            }

            tenantContext.Set(tenantId, locationId);
        }

        await _next(context);
    }
}
