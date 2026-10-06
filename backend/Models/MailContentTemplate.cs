using System.ComponentModel.DataAnnotations;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Models;

/// <summary>Per-tenant email body/subject for check-in, check-out, and appointment notifications.</summary>
public class MailContentTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Null = tenant default template; set = location-specific override.</summary>
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    public MailContentKind Kind { get; set; }

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
