using System.Text.Json;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Menu keys mirror the frontend sidebar catalog. Role defines the ceiling;
/// a per-user allowlist may only narrow that set.
/// </summary>
public static class MenuAccess
{
    public static readonly string[] AllKeys =
    [
        "dashboard", "reception", "host", "visitors", "inside", "expected",
        "reports", "departments", "purposes", "locations", "feedback", "sites", "users", "settings"
    ];

    private static readonly HashSet<string> Known = new(AllKeys, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string[]> RoleMenus = new(StringComparer.OrdinalIgnoreCase)
    {
        [AppRoles.SuperAdmin] = AllKeys,
        [AppRoles.Admin] = AllKeys,
        [AppRoles.Reception] = ["dashboard", "reception", "visitors", "inside", "expected", "reports"],
        [AppRoles.Security] = ["dashboard", "visitors", "inside", "expected"],
        [AppRoles.Host] = ["dashboard", "host", "visitors", "inside", "expected"],
    };

    public static IReadOnlyList<string> KeysForRole(string role) =>
        RoleMenus.TryGetValue(role, out var keys) ? keys : Array.Empty<string>();

    public static IReadOnlyList<string> NormalizeForRole(string role, IEnumerable<string>? requested)
    {
        var ceiling = new HashSet<string>(KeysForRole(role), StringComparer.OrdinalIgnoreCase);
        if (requested is null) return ceiling.OrderBy(k => Array.IndexOf(AllKeys, k)).ToList();

        var selected = requested
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            // Retired hub keys (Security Desk / Emergency) — map to Currently Inside when still in role ceiling.
            .Select(k => k is "security" or "emergency" ? "inside" : k)
            .Where(k => Known.Contains(k) && ceiling.Contains(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => Array.IndexOf(AllKeys, k))
            .ToList();

        // Empty selection falls back to the full role set so users are never locked out of the app.
        return selected.Count == 0 ? ceiling.OrderBy(k => Array.IndexOf(AllKeys, k)).ToList() : selected;
    }

    public static string? Serialize(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return null;
        return JsonSerializer.Serialize(keys);
    }

    public static IReadOnlyList<string> Deserialize(string? json, string role)
    {
        if (string.IsNullOrWhiteSpace(json))
            return KeysForRole(role).ToList();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return NormalizeForRole(role, parsed);
        }
        catch (JsonException)
        {
            return KeysForRole(role).ToList();
        }
    }
}
