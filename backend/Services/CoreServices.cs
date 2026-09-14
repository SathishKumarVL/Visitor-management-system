using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
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
