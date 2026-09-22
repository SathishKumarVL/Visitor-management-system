using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LogoPath { get; set; }

    [MaxLength(32)]
    public string PrimaryColor { get; set; } = "#0F766E";

    [MaxLength(32)]
    public string SecondaryColor { get; set; } = "#14B8A6";

    [MaxLength(500)]
    public string? FaviconPath { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Resolved per-request. Never trust client-supplied tenant without auth binding.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? LocationId { get; }
    void Set(Guid tenantId, Guid? locationId = null);
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? LocationId { get; private set; }

    public void Set(Guid tenantId, Guid? locationId = null)
    {
        TenantId = tenantId;
        LocationId = locationId;
    }
}
