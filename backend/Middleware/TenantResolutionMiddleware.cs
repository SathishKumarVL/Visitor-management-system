using System.Security.Claims;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Middleware;

/// <summary>
/// Binds tenant context from authenticated user claims. Client headers are ignored.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantId = TenantClaims.ResolveTenantId(context.User);
            Guid? siteId = null;
            var siteRaw = context.User.FindFirstValue(TenantClaims.SiteIdClaim);
            if (Guid.TryParse(siteRaw, out var sid)) siteId = sid;
            tenantContext.Set(tenantId, siteId);
        }

        await _next(context);
    }
}
