using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

/// <summary>
/// Tenant-wide sequence for allocated visitor pass codes.
/// Format: <c>{Prefix}{Year}-{number:D6}</c> e.g. <c>VMS-2026-000184</c>
/// (Year comes from the series when set, otherwise the current calendar year).
/// </summary>
public class PassNumberSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Null = tenant default series; set = location-specific series.</summary>
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    [Required, MaxLength(20)]
    public string Prefix { get; set; } = "VMS-";

    /// <summary>Fixed year stamped into codes; null uses the current calendar year at allocate time.</summary>
    public int? Year { get; set; }

    public int StartNumber { get; set; } = 1;
    public int EndNumber { get; set; } = 999999;

    /// <summary>
    /// Last allocated number (for display). Allocation picks the lowest free number in
    /// [StartNumber, EndNumber] among visits that still hold a badge; check-out releases it.
    /// </summary>
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

/// <summary>
/// Short-lived reservation created at allocate time so concurrent check-ins cannot claim the same
/// free pool number before the visit row is saved. Released on check-out / cancel / reject, or
/// after a short TTL if registration never completes.
/// </summary>
public class PassNumberHold
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Formatted code without the number (e.g. "VMS-2026-"); numbers are only unique within one key.</summary>
    [MaxLength(40)]
    public string? SeriesKey { get; set; }

    public int Number { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A device that receives push notifications for a user. Browser (Web Push) subscriptions use Platform "web".</summary>
public class DevicePushToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>For Web Push, the browser's push service endpoint URL.</summary>
    [Required, MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Platform { get; set; } = "unknown";

    /// <summary>Web Push subscription public key (base64url).</summary>
    [MaxLength(200)]
    public string? P256dh { get; set; }

    /// <summary>Web Push subscription auth secret (base64url).</summary>
    [MaxLength(100)]
    public string? Auth { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
