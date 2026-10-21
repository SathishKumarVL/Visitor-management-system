using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

/// <summary>
/// An external API searched from a purpose field (for example a school's student list).
/// Configured by SuperAdmin in Settings; the API key is stored encrypted and never sent to the browser.
/// </summary>
public class LookupSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute URL; <c>{query}</c> is replaced by the URL-encoded search text.</summary>
    [MaxLength(1000)]
    public string SearchUrl { get; set; } = string.Empty;

    /// <summary>GET or POST.</summary>
    [MaxLength(10)]
    public string HttpMethod { get; set; } = "GET";

    /// <summary>JSON sent with POST; <c>{query}</c> and <c>{field:Label}</c> are filled in as JSON strings.</summary>
    [MaxLength(4000)]
    public string? BodyTemplate { get; set; }

    [MaxLength(100)]
    public string? ApiKeyHeader { get; set; }

    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Dot path to the list of records in the response; empty when the response itself is the list.</summary>
    [MaxLength(200)]
    public string? ResultsPath { get; set; }

    [MaxLength(200)]
    public string IdPath { get; set; } = string.Empty;

    /// <summary>Text shown and saved for a picked record, e.g. "{name} - Class {class}".</summary>
    [MaxLength(300)]
    public string DisplayTemplate { get; set; } = string.Empty;

    /// <summary>JSON array of <see cref="LookupSavedField"/>.</summary>
    public string? SavedFieldsJson { get; set; }

    /// <summary>Dot path to the value(s) compared with the visitor, e.g. "guardians.mobile".</summary>
    [MaxLength(200)]
    public string? MatchPath { get; set; }

    /// <summary><see cref="LookupMatchTargets"/>; null turns the check off.</summary>
    [MaxLength(20)]
    public string? MatchAgainst { get; set; }

    public bool BlockOnMismatch { get; set; }

    public int MinChars { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 5;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(256)]
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}

public class LookupSavedField
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public static class LookupMatchTargets
{
    public const string Mobile = "mobile";
    public const string Email = "email";
}
