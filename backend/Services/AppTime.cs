namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Business time zone for everything the server shows or groups by day (emails, reports, "today").
/// Timestamps are still stored in UTC; this only affects conversion. Independent of the server's
/// own Windows time zone, which is often UTC on cloud hosts.
/// </summary>
public static class AppTime
{
    public const string DefaultTimeZoneId = "India Standard Time";

    private static TimeZoneInfo _zone = Resolve(DefaultTimeZoneId) ?? TimeZoneInfo.Local;

    public static TimeZoneInfo Zone => _zone;

    /// <summary>Accepts Windows ("India Standard Time") or IANA ("Asia/Kolkata") ids.</summary>
    public static void Configure(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return;
        _zone = Resolve(timeZoneId.Trim())
                ?? throw new InvalidOperationException($"Unknown time zone '{timeZoneId}' in App:TimeZone.");
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Converts a stored UTC value (Kind may be Unspecified when read from SQL) to business time.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            _zone);

    /// <summary>UTC instant at which the given business-time day starts.</summary>
    public static DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), _zone);

    private static TimeZoneInfo? Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return id == DefaultTimeZoneId && TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Kolkata", out var iana)
                ? iana
                : null;
        }
    }
}
