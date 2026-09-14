using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    [MaxLength(128)]
    public string? ReplacedByTokenHash { get; set; }

    [MaxLength(100)]
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
