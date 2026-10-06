using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

/// <summary>
/// Credential for another application calling <c>/integration</c> endpoints with an <c>X-Api-Key</c> header.
/// Only a SHA-256 hash of the key is stored; the plain key is shown once when it is created.
/// </summary>
public class IntegrationApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>First characters of the key, so admins can tell keys apart without seeing them.</summary>
    [MaxLength(16)]
    public string KeyPrefix { get; set; } = string.Empty;

    [MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Comma-separated <see cref="IntegrationScopes"/> values.</summary>
    [MaxLength(200)]
    public string Scopes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    [MaxLength(256)]
    public string? RevokedBy { get; set; }
}

public static class IntegrationScopes
{
    public const string Reports = "reports";
    public const string Employees = "employees";
    public static readonly string[] All = [Reports, Employees];
}
