using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync();
    Task<SettingsDto> GetPublicBrandingAsync();
    Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName);
    Task<SettingsDto> UploadLogoAsync(IFormFile file, string? userName);
    Task<string> GetValueAsync(string key, string fallback);
}

public class SettingsService : ISettingsService
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<Guid, (SettingsDto Dto, DateTime AtUtc)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> AllowedThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiaano", "ocean", "forest", "slate", "sunrise"
    };

    private static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "inter", "sourceSans", "ibmPlex", "nunito"
    };

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;
    private readonly IMediaStorageService _media;

    public SettingsService(
        ApplicationDbContext db,
        ITenantContext tenant,
        IWebHostEnvironment env,
        IMediaStorageService media)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
        _media = media;
    }

    private Guid EffectiveTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required for settings.");

    public async Task<SettingsDto> GetAsync()
    {
        var tenantId = EffectiveTenantId;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(tenantId, out var hit) && DateTime.UtcNow - hit.AtUtc < CacheTtl)
                return hit.Dto;
        }

        var map = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
        var dto = Map(map);
        lock (CacheLock)
        {
            Cache[tenantId] = (dto, DateTime.UtcNow);
        }
        return dto;
    }

    /// <summary>Public branding for anonymous login screen — default tenant only, explicit scope.</summary>
    public async Task<SettingsDto> GetPublicBrandingAsync()
    {
        var tenantId = WellKnownTenants.TiaanoId;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(tenantId, out var hit) && DateTime.UtcNow - hit.AtUtc < CacheTtl)
                return hit.Dto;
        }

        var map = await _db.SystemSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        var dto = Map(map);
        lock (CacheLock)
        {
            Cache[tenantId] = (dto, DateTime.UtcNow);
        }
        return dto;
    }

    public async Task<string> GetValueAsync(string key, string fallback)
    {
        _ = EffectiveTenantId;
        var item = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return item?.Value ?? fallback;
    }

    public async Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName)
    {
        var tenantId = EffectiveTenantId;
        async Task Upsert(string key, string value)
        {
            var row = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
            if (row is null)
            {
                _db.SystemSettings.Add(new SystemSetting { TenantId = tenantId, Key = key, Value = value, UpdatedBy = userName });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = userName;
            }
        }

        await Upsert("CompanyName", dto.CompanyName?.Trim() ?? "TIAANO");
        await Upsert("LogoPath", string.IsNullOrWhiteSpace(dto.LogoPath) ? "/branding/tiaano-logo.png" : dto.LogoPath.Trim());
        await Upsert("ThemePreset", NormalizeTheme(dto.ThemePreset));
        await Upsert("FontPreset", NormalizeFont(dto.FontPreset));
        await Upsert("VisitorIdPrefix", dto.VisitorIdPrefix);
        await Upsert("VisitorPassValidityHours", dto.VisitorPassValidityHours.ToString());
        await Upsert("ApprovalRequired", dto.ApprovalRequired.ToString().ToLowerInvariant());
        await Upsert("WalkInApprovalRequired", dto.WalkInApprovalRequired.ToString().ToLowerInvariant());
        await Upsert("PhotoRequired", dto.PhotoRequired.ToString().ToLowerInvariant());
        await Upsert("IdVerificationRequired", dto.IdVerificationRequired.ToString().ToLowerInvariant());
        await Upsert("MaxVisitDurationWarningMinutes", dto.MaxVisitDurationWarningMinutes.ToString());
        await Upsert("SessionTimeoutMinutes", dto.SessionTimeoutMinutes.ToString());
        await _db.SaveChangesAsync();
        ClearCache(tenantId);
        return await GetAsync();
    }

    public async Task<SettingsDto> UploadLogoAsync(IFormFile file, string? userName)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("Choose an image file to upload.");
        if (file.Length > 5_000_000)
            throw new InvalidOperationException("Logo must be under 5MB.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (!_media.IsAllowedImage(bytes, out var contentType))
            throw new InvalidOperationException("Logo must be a valid JPEG, PNG, or WebP image.");

        var tenantId = EffectiveTenantId;
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var brandingDir = Path.Combine(webRoot, "branding", "tenants", tenantId.ToString("N"));
        Directory.CreateDirectory(brandingDir);

        foreach (var old in Directory.EnumerateFiles(brandingDir, "logo.*"))
            File.Delete(old);

        var ext = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"logo{ext}";
        var fullPath = Path.Combine(brandingDir, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes);

        var publicPath = $"/branding/tenants/{tenantId:N}/{fileName}";
        var current = await GetAsync();
        current.LogoPath = publicPath;
        return await UpdateAsync(current, userName);
    }

    private static void ClearCache(Guid tenantId)
    {
        lock (CacheLock)
        {
            Cache.Remove(tenantId);
        }
    }

    private static string NormalizeTheme(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "tiaano" : value.Trim();
        return AllowedThemes.Contains(key) ? AllowedThemes.First(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) : "tiaano";
    }

    private static string NormalizeFont(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "inter" : value.Trim();
        return AllowedFonts.Contains(key) ? AllowedFonts.First(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) : "inter";
    }

    private static SettingsDto Map(Dictionary<string, string> map) => new()
    {
        CompanyName = map.GetValueOrDefault("CompanyName", "TIAANO"),
        LogoPath = map.GetValueOrDefault("LogoPath", "/branding/tiaano-logo.png"),
        ThemePreset = NormalizeTheme(map.GetValueOrDefault("ThemePreset", "tiaano")),
        FontPreset = NormalizeFont(map.GetValueOrDefault("FontPreset", "inter")),
        VisitorIdPrefix = map.GetValueOrDefault("VisitorIdPrefix", "TIA"),
        VisitorPassValidityHours = int.TryParse(map.GetValueOrDefault("VisitorPassValidityHours"), out var h) ? h : 12,
        ApprovalRequired = bool.TryParse(map.GetValueOrDefault("ApprovalRequired", "false"), out var ar) && ar,
        WalkInApprovalRequired = bool.TryParse(map.GetValueOrDefault("WalkInApprovalRequired", "false"), out var war) && war,
        PhotoRequired = bool.TryParse(map.GetValueOrDefault("PhotoRequired", "false"), out var pr) && pr,
        IdVerificationRequired = bool.TryParse(map.GetValueOrDefault("IdVerificationRequired", "false"), out var idr) && idr,
        MaxVisitDurationWarningMinutes = int.TryParse(map.GetValueOrDefault("MaxVisitDurationWarningMinutes"), out var m) ? m : 240,
        SessionTimeoutMinutes = int.TryParse(map.GetValueOrDefault("SessionTimeoutMinutes"), out var s) ? s : 480
    };
}
