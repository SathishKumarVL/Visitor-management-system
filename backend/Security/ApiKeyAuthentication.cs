using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;

namespace Tiaano.Vms.Api.Security;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ScopeClaim = "scope";
    public const string KeyIdClaim = "apiKeyId";

    /// <summary>Requests under this path authenticate with an API key only; all other paths use JWT only.</summary>
    public const string PathPrefix = "/integration";

    public const string ReportsPolicy = "Integration.Reports";
    public const string EmployeesPolicy = "Integration.Employees";
}

/// <summary>
/// Authenticates other applications by the <c>X-Api-Key</c> header. The resulting identity carries the key's
/// tenant and scopes but no user roles, so it cannot satisfy any role-protected endpoint.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApplicationDbContext _db;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var values))
            return AuthenticateResult.NoResult();

        var presented = values.ToString().Trim();
        if (presented.Length == 0)
            return AuthenticateResult.Fail("API key is empty.");

        var hash = IntegrationKeyService.HashKey(presented);
        var key = await _db.IntegrationApiKeys.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash);
        if (key is null || !key.IsActive)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        var now = DateTime.UtcNow;
        if (key.LastUsedAt is null || key.LastUsedAt < now.AddMinutes(-1))
        {
            await _db.IntegrationApiKeys.IgnoreQueryFilters()
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, $"api:{key.Name}"),
            new(ApiKeyDefaults.KeyIdClaim, key.Id.ToString()),
            new(TenantClaims.TenantIdClaim, key.TenantId.ToString()),
        };
        claims.AddRange(IntegrationKeyService.ParseScopes(key.Scopes)
            .Select(s => new Claim(ApiKeyDefaults.ScopeClaim, s)));

        var identity = new ClaimsIdentity(claims, ApiKeyDefaults.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new
        {
            success = false,
            message = $"Send a valid API key in the {ApiKeyDefaults.HeaderName} header."
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new { success = false, message = "This API key is not allowed to use this endpoint." });
    }
}
