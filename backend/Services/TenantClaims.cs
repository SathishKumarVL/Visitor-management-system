using System.Security.Claims;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public static class TenantClaims
{
    public const string TenantIdClaim = "tenantId";
    public const string SiteIdClaim = "siteId";

    /// <summary>
    /// Resolves tenant from request context or JWT. Does not soft-fallback for authenticated identity
    /// when claim is missing — callers must treat Guid.Empty as failure.
    /// </summary>
    public static Guid ResolveTenantId(ClaimsPrincipal? user, ITenantContext? tenantContext = null)
    {
        if (tenantContext?.TenantId is Guid ctx && ctx != Guid.Empty)
            return ctx;

        var raw = user?.FindFirstValue(TenantIdClaim);
        if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
            return id;

        return Guid.Empty;
    }

    public static Guid RequireTenantId(ClaimsPrincipal? user, ITenantContext? tenantContext = null)
    {
        var id = ResolveTenantId(user, tenantContext);
        if (id == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant context is required.");
        return id;
    }
}
