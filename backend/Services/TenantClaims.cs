using System.Security.Claims;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public static class TenantClaims
{
    public const string TenantIdClaim = "tenantId";
    public const string SiteIdClaim = "siteId";

    public static Guid ResolveTenantId(ClaimsPrincipal? user, ITenantContext? tenantContext = null)
    {
        if (tenantContext?.TenantId is Guid ctx && ctx != Guid.Empty)
            return ctx;

        var raw = user?.FindFirstValue(TenantIdClaim);
        if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
            return id;

        return WellKnownTenants.TiaanoId;
    }
}
