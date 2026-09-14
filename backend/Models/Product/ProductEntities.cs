using System.ComponentModel.DataAnnotations;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Models.Product;

public class ProductModule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(80)]
    public string ModuleKey { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Version { get; set; } = "1.0.0";

    public bool IsCore { get; set; }
}

public class TenantModuleEntitlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(80)]
    public string ModuleKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
}

public class TenantLicense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(40)]
    public string Edition { get; set; } = "Starter"; // Starter | Professional | Enterprise

    public int MaxSites { get; set; } = 1;
    public int MaxUsers { get; set; } = 25;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(100)]
    public string Status { get; set; } = "Active";

    /// <summary>Graceful expiry: data remains readable; write features may be limited.</summary>
    public int GraceDaysAfterExpiry { get; set; } = 30;
}

public class FeatureFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
    public string? ConfigurationJson { get; set; }
}

public class ApplicationRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(40)]
    public string Version { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? DatabaseVersion { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
