using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddress);
    /// <summary>Signs in as the login whose registered face matches; null when no face matches.</summary>
    Task<LoginResponse?> FaceLoginAsync(FaceLoginRequest request, string? ipAddress);
    Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal);
    Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress);
    Task LogoutAsync(ClaimsPrincipal principal, string? refreshToken);
    Task<LoginResponse> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request, string? ipAddress);
    /// <summary>Emails a reset link when the account exists; silently does nothing otherwise.</summary>
    Task RequestPasswordResetAsync(string usernameOrEmail, string? requestOrigin);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}

/// <summary>Thrown when the password was right but the account is temporarily locked.</summary>
public sealed class AccountLockedException(string message) : Exception(message);

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly INotificationService _notifications;
    private readonly IUserFaceLoginService _faceLogins;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config,
        IHostEnvironment env,
        ApplicationDbContext db,
        ITenantContext tenant,
        INotificationService notifications,
        IUserFaceLoginService faceLogins,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _env = env;
        _db = db;
        _tenant = tenant;
        _notifications = notifications;
        _faceLogins = faceLogins;
        _logger = logger;
    }

    /// <summary>Case-insensitive match on username, or on email when the input looks like one.</summary>
    private async Task<ApplicationUser?> FindByLoginAsync(string? login)
    {
        var value = (login ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        var users = _userManager.Users.IgnoreQueryFilters().Include(u => u.Department).Include(u => u.Location);
        var normalizedName = _userManager.NormalizeName(value);
        var user = await users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedName);
        if (user is null && value.Contains('@'))
        {
            var normalizedEmail = _userManager.NormalizeEmail(value);
            user = await users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        }
        return user;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddress)
    {
        var user = await FindByLoginAsync(request.Username);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (user.TenantId == Guid.Empty)
        {
            return null;
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            var end = await _userManager.GetLockoutEndDateAsync(user);
            var minutes = end is null ? 15 : Math.Max(1, (int)Math.Ceiling((end.Value - DateTimeOffset.UtcNow).TotalMinutes));
            throw new AccountLockedException(
                $"This account is locked after too many wrong passwords. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}, use Forgot password, or ask an administrator to reset it.");
        }
        if (!result.Succeeded)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var response = await IssueTokensAsync(user, roles, request.RememberMe, ipAddress);
        return response;
    }

    public async Task<LoginResponse?> FaceLoginAsync(FaceLoginRequest request, string? ipAddress)
    {
        var match = await _faceLogins.MatchAsync(request.PhotoBase64);
        if (match is null)
        {
            _logger.LogInformation("Face sign-in from {Ip}: no registered face matched.", ipAddress);
            return null;
        }

        var user = await _userManager.Users.IgnoreQueryFilters()
            .Include(u => u.Department).Include(u => u.Location)
            .FirstOrDefaultAsync(u => u.Id == match.UserId);
        if (user is null || user.TenantId == Guid.Empty)
            return null;
        if (!user.IsActive)
            throw new InvalidOperationException("This login is disabled. Ask an administrator to activate it.");
        if (await _userManager.IsLockedOutAsync(user))
            throw new AccountLockedException(
                "This login is locked after too many wrong passwords. Try again later or ask an administrator to reset it.");
        // Whoever signs in by face may not know the current password, so they could never complete the change.
        if (user.MustChangePassword)
            throw new InvalidOperationException(
                "This login must set a new password first. Sign in once with the username and password, then use face sign-in.");

        await _faceLogins.MarkUsedAsync(match.FaceId);
        _logger.LogInformation(
            "Face sign-in: {Person} signed in as {UserName} (similarity {Similarity:F3}) from {Ip}.",
            match.PersonName, user.UserName, match.Similarity, ipAddress);

        var roles = await _userManager.GetRolesAsync(user);
        var response = await IssueTokensAsync(user, roles, request.RememberMe, ipAddress);
        response.FacePersonName = match.PersonName;
        return response;
    }

    public async Task RequestPasswordResetAsync(string usernameOrEmail, string? requestOrigin)
    {
        var user = await FindByLoginAsync(usernameOrEmail);
        if (user is null || !user.IsActive || user.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogInformation("Password reset requested for an unknown, inactive or email-less account.");
            return;
        }

        var baseUrl = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) && _env.IsDevelopment())
            baseUrl = requestOrigin?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Never build the link from the request's Host header: an attacker could point it at their own site.
            _logger.LogError("App:PublicBaseUrl is not configured; password reset email was not sent.");
            return;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{baseUrl}/reset-password?uid={Uri.EscapeDataString(user.Id)}&token={encoded}";

        _tenant.Set(user.TenantId, user.LocationId);
        var key = $"pwd-reset:{user.Id}:{Guid.NewGuid():N}";
        var body =
            $"Hello {user.FullName},\n\n" +
            "We received a request to reset your Visitor Management password.\n\n" +
            $"Open this link to choose a new password (valid for 1 hour):\n{link}\n\n" +
            "If you did not ask for this, you can ignore this email; your password will not change.\n";
        await _notifications.NotifyAsync("Email", user.Email!, "Reset your Visitor Management password", body, key, user.LocationId);

        // The outbox keeps message bodies; do not leave a usable reset link stored once it has been delivered.
        await _db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.IdempotencyKey == key && n.IsSent)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Body, "[Password reset link removed after delivery]"));
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        const string invalidLink = "This reset link is invalid or has expired. Please request a new one.";
        var user = await _userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (user is null || !user.IsActive)
            throw new InvalidOperationException(invalidLink);

        string token;
        try
        {
            token = Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(invalidLink);
        }

        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
                throw new InvalidOperationException(invalidLink);
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        var tokens = await _db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var user = await _userManager.Users.Include(u => u.Department).Include(u => u.Location).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || !user.IsActive) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return MapUser(user, roles);
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens.IgnoreQueryFilters()
            .Include(t => t.User).ThenInclude(u => u!.Department)
            .Include(t => t.User).ThenInclude(u => u!.Location)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (existing is null || !existing.IsActive || existing.User is null || !existing.User.IsActive)
            return null;
        if (existing.User.TenantId == Guid.Empty)
            return null;

        existing.RevokedAt = DateTime.UtcNow;
        var roles = await _userManager.GetRolesAsync(existing.User);
        var response = await IssueTokensAsync(existing.User, roles, rememberMe: false, ipAddress, existing);
        await _db.SaveChangesAsync();
        return response;
    }

    public async Task LogoutAsync(ClaimsPrincipal principal, string? refreshToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = HashToken(refreshToken);
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (token is not null && token.RevokedAt is null)
                token.RevokedAt = DateTime.UtcNow;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var active = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
            foreach (var t in active) t.RevokedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<LoginResponse> ChangePasswordAsync(
        ClaimsPrincipal principal, ChangePasswordRequest request, string? ipAddress)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Not authenticated.");
        var user = await _userManager.Users.IgnoreQueryFilters()
            .Include(u => u.Department)
            .Include(u => u.Location)
            .FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
            throw new InvalidOperationException("Current password is incorrect.");
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("New password cannot be the same as the current password.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Revoke previous sessions, then issue a fresh JWT without the mcp claim.
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var roles = await _userManager.GetRolesAsync(user);
        return await IssueTokensAsync(user, roles, rememberMe: false, ipAddress);
    }

    private async Task<LoginResponse> IssueTokensAsync(
        ApplicationUser user,
        IList<string> roles,
        bool rememberMe,
        string? ipAddress,
        RefreshToken? replaced = null)
    {
        var accessMinutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 30;
        var refreshDays = rememberMe ? 14 : 7;
        var expires = DateTime.UtcNow.AddMinutes(accessMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(refreshDays);
        var accessToken = GenerateJwt(user, roles, expires);
        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefresh),
            ExpiresAt = refreshExpires,
            CreatedByIp = ipAddress
        };
        if (replaced is not null)
            replaced.ReplacedByTokenHash = entity.TokenHash;

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();

        return new LoginResponse
        {
            Token = accessToken,
            RefreshToken = rawRefresh,
            ExpiresAt = expires,
            RefreshExpiresAt = refreshExpires,
            User = MapUser(user, roles)
        };
    }

    private string GenerateJwt(ApplicationUser user, IList<string> roles, DateTime expires)
    {
        var signingKey = SecretConfiguration.GetRequiredJwtSigningKey(_config, _env);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("fullName", user.FullName),
            new("mcp", user.MustChangePassword ? "1" : "0")
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (user.DepartmentId.HasValue)
            claims.Add(new Claim("departmentId", user.DepartmentId.Value.ToString()));
        // Tenant claim is mandatory for authenticated API access (middleware fails closed without it).
        claims.Add(new Claim("tenantId", user.TenantId.ToString()));
        if (user.LocationId.HasValue)
            claims.Add(new Claim("locationId", user.LocationId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            notBefore: DateTime.UtcNow,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static UserDto MapUser(ApplicationUser user, IList<string> roles)
    {
        var primaryRole = roles.FirstOrDefault() ?? string.Empty;
        return new()
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList(),
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            LocationId = user.LocationId,
            LocationName = user.Location?.Name,
            AllowedMenuKeys = MenuAccess.Deserialize(user.AllowedMenuKeysJson, primaryRole),
            MustChangePassword = user.MustChangePassword,
            IsActive = user.IsActive
        };
    }
}
