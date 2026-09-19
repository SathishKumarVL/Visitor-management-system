using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

/// <summary>
/// Tenant (optional site) sequence for allocated visitor pass codes.
/// Format: <c>{Prefix}{Year}-{number:D6}</c> e.g. <c>VMS-2026-000184</c>
/// (Year comes from the series when set, otherwise the current calendar year).
/// </summary>
public class PassNumberSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    [Required, MaxLength(20)]
    public string Prefix { get; set; } = "VMS-";

    /// <summary>Fixed year stamped into codes; null uses the current calendar year at allocate time.</summary>
    public int? Year { get; set; }

    public int StartNumber { get; set; } = 1;
    public int EndNumber { get; set; } = 999999;

    /// <summary>Last allocated number. Next issue is CurrentNumber + 1 when CurrentNumber &lt; EndNumber.</summary>
    public int CurrentNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<PassNumberAllocation> Allocations { get; set; } = new List<PassNumberAllocation>();
}

/// <summary>
/// Optional per-user contiguous range within a series. When active and not exhausted,
/// <see cref="Services.PassNumberService"/> allocates from this row instead of the series default counter.
/// </summary>
public class PassNumberAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid SeriesId { get; set; }
    public PassNumberSeries Series { get; set; } = null!;

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int StartNumber { get; set; }
    public int EndNumber { get; set; }
    public int CurrentNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Optional device token for push delivery (stub foundation).</summary>
public class DevicePushToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Platform { get; set; } = "unknown";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
