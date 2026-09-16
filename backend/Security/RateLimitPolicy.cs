namespace Tiaano.Vms.Api.Security;

/// <summary>
/// Single source of truth for login rate limiting so the relaxed local-development behaviour
/// cannot be inherited by any other environment.
/// </summary>
public static class RateLimitPolicy
{
    public const string LoginPolicyName = "login";
    public const int LoginPermitLimit = 10;
    public const int LoginQueueLimit = 0;

    public static TimeSpan LoginWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Environments allowed to run without the login limiter. Matching is exact and case-sensitive:
    /// anything unrecognised — including an unset environment name — stays rate limited.
    /// </summary>
    private static readonly string[] RelaxedEnvironments = ["Development", "Testing"];

    public static bool IsLoginRateLimitRelaxed(string? environmentName) =>
        environmentName is not null && RelaxedEnvironments.Contains(environmentName, StringComparer.Ordinal);
}
