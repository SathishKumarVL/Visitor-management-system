using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Tiaano.Vms.Api.Configuration;

/// <summary>
/// Resolves and validates secret configuration. Secrets must come from User Secrets,
/// environment variables, or an external vault — never from committed source.
/// Compatible with Key Vault / Secrets Manager via standard ASP.NET Core configuration.
/// </summary>
public static class SecretConfiguration
{
    public const int MinSymmetricKeyBytes = 32;

    public static string GetRequiredJwtSigningKey(IConfiguration config, IHostEnvironment env)
    {
        var key = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Required configuration 'Jwt:Key' is missing. Provide it via User Secrets, environment variables, or a secret store.");

        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < MinSymmetricKeyBytes)
            throw new InvalidOperationException("Jwt:Key is too short. Use at least 32 bytes of high-entropy key material.");

        if (env.IsProduction() && IsPlaceholder(key))
            throw new InvalidOperationException("Jwt:Key appears to be a placeholder. Set a production secret before starting.");

        return key;
    }

    public static string GetRequiredEncryptionKey(IConfiguration config, IHostEnvironment env)
    {
        var key = config["Security:DataProtectionKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Required configuration 'Security:DataProtectionKey' is missing. Provide it via User Secrets, environment variables, or a secret store.");

        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < 16)
            throw new InvalidOperationException("Security:DataProtectionKey is too short.");

        if (env.IsProduction() && IsPlaceholder(key))
            throw new InvalidOperationException("Security:DataProtectionKey appears to be a placeholder. Set a production secret before starting.");

        return key;
    }

    public static string? GetSeedPasswordForCreateOnly(IConfiguration config)
    {
        var password = config["Seed:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(password))
            return null;
        if (IsPlaceholder(password))
            return null;
        return password;
    }

    public static void ValidateSmtpSecurity(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsProduction() && config.GetValue("Smtp:IgnoreSslErrors", false))
            throw new InvalidOperationException("Smtp:IgnoreSslErrors must not be enabled in Production.");
    }

    public static void ValidateProductionSecretsAbsentFromDefaults(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsProduction())
            return;

        // Production must not rely on empty/placeholder secrets already checked above.
        ValidateSmtpSecurity(config, env);
        _ = GetRequiredJwtSigningKey(config, env);
        _ = GetRequiredEncryptionKey(config, env);
    }

    public static TokenValidationParameters CreateJwtValidationParameters(IConfiguration config, string signingKey)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = config["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is required."),
            ValidAudience = config["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required."),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            // Algorithm constrained by SigningCredentials (HmacSha256) at token creation.
        };
    }

    private static bool IsPlaceholder(string value)
    {
        var v = value.Trim();
        return v.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)
               || v.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
               || v.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
               || v.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }
}
