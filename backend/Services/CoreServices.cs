using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entity, string? entityId, string? description, ClaimsPrincipal? user = null, string? ip = null);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, string entity, string? entityId, string? description, ClaimsPrincipal? user = null, string? ip = null)
    {
        user ??= _http.HttpContext?.User;
        ip ??= _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = user?.Identity?.Name ?? user?.FindFirstValue(ClaimTypes.Name),
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}

public interface ISettingsService
{
    Task<SettingsDto> GetAsync();
    Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName);
    Task<string> GetValueAsync(string key, string fallback);
}

public class SettingsService : ISettingsService
{
    private static readonly object CacheLock = new();
    private static SettingsDto? _cache;
    private static DateTime _cacheAtUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public SettingsService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<SettingsDto> GetAsync()
    {
        lock (CacheLock)
        {
            if (_cache is not null && DateTime.UtcNow - _cacheAtUtc < CacheTtl)
                return _cache;
        }

        var map = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
        var dto = Map(map);
        lock (CacheLock)
        {
            _cache = dto;
            _cacheAtUtc = DateTime.UtcNow;
        }
        return dto;
    }

    public async Task<string> GetValueAsync(string key, string fallback)
    {
        var item = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return item?.Value ?? fallback;
    }

    public async Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName)
    {
        async Task Upsert(string key, string value)
        {
            var row = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
            if (row is null)
            {
                _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedBy = userName });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = userName;
            }
        }

        await Upsert("CompanyName", dto.CompanyName);
        await Upsert("LogoPath", dto.LogoPath);
        await Upsert("VisitorIdPrefix", dto.VisitorIdPrefix);
        await Upsert("VisitorPassValidityHours", dto.VisitorPassValidityHours.ToString());
        await Upsert("ApprovalRequired", dto.ApprovalRequired.ToString().ToLowerInvariant());
        await Upsert("WalkInApprovalRequired", dto.WalkInApprovalRequired.ToString().ToLowerInvariant());
        await Upsert("PhotoRequired", dto.PhotoRequired.ToString().ToLowerInvariant());
        await Upsert("IdVerificationRequired", dto.IdVerificationRequired.ToString().ToLowerInvariant());
        await Upsert("MaxVisitDurationWarningMinutes", dto.MaxVisitDurationWarningMinutes.ToString());
        await Upsert("DefaultEntryGate", dto.DefaultEntryGate);
        await Upsert("DefaultExitGate", dto.DefaultExitGate);
        await Upsert("SessionTimeoutMinutes", dto.SessionTimeoutMinutes.ToString());
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SettingsChanged", "SystemSetting", null, "System settings updated");
        lock (CacheLock)
        {
            _cache = null;
            _cacheAtUtc = DateTime.MinValue;
        }
        return await GetAsync();
    }

    private static SettingsDto Map(Dictionary<string, string> map) => new()
    {
        CompanyName = map.GetValueOrDefault("CompanyName", "TIAANO"),
        LogoPath = map.GetValueOrDefault("LogoPath", "/branding/tiaano-logo.png"),
        VisitorIdPrefix = map.GetValueOrDefault("VisitorIdPrefix", "TIA"),
        VisitorPassValidityHours = int.TryParse(map.GetValueOrDefault("VisitorPassValidityHours"), out var h) ? h : 12,
        ApprovalRequired = bool.TryParse(map.GetValueOrDefault("ApprovalRequired", "false"), out var ar) && ar,
        WalkInApprovalRequired = bool.TryParse(map.GetValueOrDefault("WalkInApprovalRequired", "false"), out var war) && war,
        PhotoRequired = bool.TryParse(map.GetValueOrDefault("PhotoRequired", "false"), out var pr) && pr,
        IdVerificationRequired = bool.TryParse(map.GetValueOrDefault("IdVerificationRequired", "false"), out var idr) && idr,
        MaxVisitDurationWarningMinutes = int.TryParse(map.GetValueOrDefault("MaxVisitDurationWarningMinutes"), out var m) ? m : 240,
        DefaultEntryGate = map.GetValueOrDefault("DefaultEntryGate", "Main Gate"),
        DefaultExitGate = map.GetValueOrDefault("DefaultExitGate", "Main Gate"),
        SessionTimeoutMinutes = int.TryParse(map.GetValueOrDefault("SessionTimeoutMinutes"), out var s) ? s : 480
    };
}

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal);
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

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.Users.Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.UserName == request.Username);
        if (user is null || !user.IsActive) return null;

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var expires = DateTime.UtcNow.AddHours(request.RememberMe ? 12 : 8);
        var token = GenerateJwt(user, roles, expires);

        await _audit.LogAsync("UserLogin", "User", user.Id, $"User {user.UserName} logged in");

        return new LoginResponse
        {
            Token = token,
            ExpiresAt = expires,
            User = MapUser(user, roles)
        };
    }

    public async Task<UserDto?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return MapUser(user, roles);
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
            new("fullName", user.FullName)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (user.DepartmentId.HasValue)
            claims.Add(new Claim("departmentId", user.DepartmentId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
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
