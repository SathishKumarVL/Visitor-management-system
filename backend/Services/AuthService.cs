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
    Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal);
    Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress);
    Task LogoutAsync(ClaimsPrincipal principal, string? refreshToken);
    Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly IAuditService _audit;
    private readonly ApplicationDbContext _db;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config,
        IHostEnvironment env,
        IAuditService audit,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _env = env;
        _audit = audit;
        _db = db;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddress)
    {
        var user = await _userManager.Users.Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.UserName == request.Username);
        if (user is null || !user.IsActive)
        {
            await _audit.LogAsync("LoginFailed", "User", null, $"Failed login for '{request.Username}'");
            return null;
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await _audit.LogAsync("LoginLockedOut", "User", user.Id, $"Account locked: {user.UserName}");
            return null;
        }
        if (!result.Succeeded)
        {
            await _audit.LogAsync("LoginFailed", "User", user.Id, $"Failed login for '{user.UserName}'");
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var response = await IssueTokensAsync(user, roles, request.RememberMe, ipAddress);
        await _audit.LogAsync("UserLogin", "User", user.Id, $"User {user.UserName} logged in");
        return response;
    }

    public async Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || !user.IsActive) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return MapUser(user, roles);
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens.Include(t => t.User).ThenInclude(u => u!.Department)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (existing is null || !existing.IsActive || existing.User is null || !existing.User.IsActive)
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
            await _audit.LogAsync("UserLogout", "User", userId, "User logged out");
        }

        await _db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Not authenticated.");
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new UnauthorizedAccessException("User not found.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Revoke refresh tokens after password change
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("PasswordChanged", "User", user.Id, "Password changed");
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
        if (user.TenantId != Guid.Empty)
            claims.Add(new Claim("tenantId", user.TenantId.ToString()));
        if (user.SiteId.HasValue)
            claims.Add(new Claim("siteId", user.SiteId.Value.ToString()));

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

    private static UserDto MapUser(ApplicationUser user, IList<string> roles) => new()
    {
        Id = user.Id,
        Username = user.UserName ?? string.Empty,
        FullName = user.FullName,
        Email = user.Email ?? string.Empty,
        Roles = roles.ToList(),
        DepartmentId = user.DepartmentId,
        DepartmentName = user.Department?.Name,
        MustChangePassword = user.MustChangePassword,
        IsActive = user.IsActive
    };
}
