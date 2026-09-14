namespace Tiaano.Vms.Api.Models;

public static class WellKnownTenants
{
    /// <summary>Stable bootstrap tenant for the first on-premise customer (TIAANO).</summary>
    public static readonly Guid TiaanoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string TiaanoCode = "TIAANO";
}
