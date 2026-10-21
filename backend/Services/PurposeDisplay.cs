using Tiaano.Vms.Api.Data;

namespace Tiaano.Vms.Api.Services;

/// <summary>Purpose names as people read them: "Others" is replaced by the reason typed for it.</summary>
public static class PurposeDisplay
{
    public static List<string> Names(IEnumerable<string?> names, string? otherText)
    {
        var typed = otherText?.Trim();
        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => !string.IsNullOrEmpty(typed)
                         && string.Equals(n!.Trim(), VisitPurposeDefaults.OthersName, StringComparison.OrdinalIgnoreCase)
                ? typed
                : n!)
            .ToList();
    }

    public static string Join(IEnumerable<string?> names, string? otherText, string separator = ", ") =>
        string.Join(separator, Names(names, otherText));
}
