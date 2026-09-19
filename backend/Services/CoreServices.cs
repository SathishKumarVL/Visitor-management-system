using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
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
    Task<SmtpRuntimeOptions> ResolveSmtpAsync(CancellationToken ct = default);
}

public sealed class SmtpRuntimeOptions
{
    public bool Enabled { get; init; } = true;
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string FromName { get; init; } = "Visitor Management";
    public bool IgnoreSslErrors { get; init; }
    public bool FallbackToPickupOnFailure { get; init; } = true;
    public string? PickupDirectory { get; init; }
    public int TimeoutSeconds { get; init; } = 45;
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
    private readonly IConfiguration _config;
    private readonly IMediaStorageService _media;

    public SettingsService(
        ApplicationDbContext db,
        ITenantContext tenant,
        IWebHostEnvironment env,
        IConfiguration config,
        IMediaStorageService media)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
        _config = config;
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
        ApplyConfigSmtpFallback(dto);
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
        var map = await _db.SystemSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        var dto = Map(map);
        // Never expose mailbox config on the public branding path.
        dto.SmtpEnabled = false;
        dto.SmtpHost = "";
        dto.SmtpUsername = "";
        dto.SmtpFromAddress = "";
        dto.SmtpFromName = "";
        dto.SmtpPassword = null;
        dto.SmtpPasswordConfigured = false;
        dto.SmtpIgnoreSslErrors = false;
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

        await Upsert("Smtp:Enabled", dto.SmtpEnabled.ToString().ToLowerInvariant());
        await Upsert("Smtp:Host", (dto.SmtpHost ?? "").Trim());
        await Upsert("Smtp:Port", Math.Clamp(dto.SmtpPort <= 0 ? 587 : dto.SmtpPort, 1, 65535).ToString());
        await Upsert("Smtp:EnableSsl", dto.SmtpEnableSsl.ToString().ToLowerInvariant());
        await Upsert("Smtp:Username", (dto.SmtpUsername ?? "").Trim());
        await Upsert("Smtp:FromAddress", (dto.SmtpFromAddress ?? "").Trim());
        await Upsert("Smtp:FromName", string.IsNullOrWhiteSpace(dto.SmtpFromName) ? "Visitor Management" : dto.SmtpFromName.Trim());
        await Upsert("Smtp:IgnoreSslErrors", dto.SmtpIgnoreSslErrors.ToString().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(dto.SmtpPassword))
        {
            var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
            await Upsert("Smtp:PasswordEncrypted", SensitiveDataHelper.Encrypt(dto.SmtpPassword.Trim(), key));
        }

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
        current.SmtpPassword = null; // preserve stored password on logo-only update
        return await UpdateAsync(current, userName);
    }

    public async Task<SmtpRuntimeOptions> ResolveSmtpAsync(CancellationToken ct = default)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        if (_tenant.TenantId is Guid tid && tid != Guid.Empty)
        {
            map = await _db.SystemSettings.AsNoTracking()
                .Where(x => x.Key.StartsWith("Smtp:"))
                .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        }

        string Pick(string key, string? configFallback = null)
        {
            if (map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            return (configFallback ?? _config[key] ?? "").Trim();
        }

        bool PickBool(string key, bool fallback)
        {
            if (map.TryGetValue(key, out var v) && bool.TryParse(v, out var b))
                return b;
            return _config.GetValue(key, fallback);
        }

        int PickInt(string key, int fallback)
        {
            if (map.TryGetValue(key, out var v) && int.TryParse(v, out var n))
                return n;
            return _config.GetValue(key, fallback);
        }

        var password = "";
        if (map.TryGetValue("Smtp:PasswordEncrypted", out var enc) && !string.IsNullOrWhiteSpace(enc))
        {
            try
            {
                var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
                password = SensitiveDataHelper.Decrypt(enc, key);
            }
            catch
            {
                password = "";
            }
        }
        if (string.IsNullOrEmpty(password))
            password = _config["Smtp:Password"] ?? "";

        return new SmtpRuntimeOptions
        {
            Enabled = PickBool("Smtp:Enabled", _config.GetValue("Smtp:Enabled", true)),
            Host = Pick("Smtp:Host"),
            Port = Math.Clamp(PickInt("Smtp:Port", 587), 1, 65535),
            EnableSsl = PickBool("Smtp:EnableSsl", true),
            Username = Pick("Smtp:Username"),
            Password = password,
            FromAddress = Pick("Smtp:FromAddress"),
            FromName = string.IsNullOrWhiteSpace(Pick("Smtp:FromName"))
                ? "Visitor Management"
                : Pick("Smtp:FromName"),
            IgnoreSslErrors = PickBool("Smtp:IgnoreSslErrors", false),
            FallbackToPickupOnFailure = _config.GetValue("Smtp:FallbackToPickupOnFailure", true),
            PickupDirectory = _config["Smtp:PickupDirectory"],
            TimeoutSeconds = Math.Clamp(_config.GetValue("Smtp:TimeoutSeconds", 45), 10, 120),
        };
    }

    private void ApplyConfigSmtpFallback(SettingsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SmtpHost))
            dto.SmtpHost = _config["Smtp:Host"] ?? "";
        if (dto.SmtpPort <= 0)
            dto.SmtpPort = _config.GetValue("Smtp:Port", 587);
        if (string.IsNullOrWhiteSpace(dto.SmtpUsername))
            dto.SmtpUsername = _config["Smtp:Username"] ?? "";
        if (string.IsNullOrWhiteSpace(dto.SmtpFromAddress))
            dto.SmtpFromAddress = _config["Smtp:FromAddress"] ?? "";
        if (string.IsNullOrWhiteSpace(dto.SmtpFromName) || dto.SmtpFromName == "Visitor Management")
        {
            var fromName = _config["Smtp:FromName"];
            if (!string.IsNullOrWhiteSpace(fromName))
                dto.SmtpFromName = fromName;
        }
        if (!dto.SmtpPasswordConfigured && !string.IsNullOrEmpty(_config["Smtp:Password"]))
            dto.SmtpPasswordConfigured = true;
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
        SessionTimeoutMinutes = int.TryParse(map.GetValueOrDefault("SessionTimeoutMinutes"), out var s) ? s : 480,
        SmtpEnabled = !bool.TryParse(map.GetValueOrDefault("Smtp:Enabled", "true"), out var se) || se,
        SmtpHost = map.GetValueOrDefault("Smtp:Host", ""),
        SmtpPort = int.TryParse(map.GetValueOrDefault("Smtp:Port"), out var sp) ? sp : 587,
        SmtpEnableSsl = !bool.TryParse(map.GetValueOrDefault("Smtp:EnableSsl", "true"), out var ssl) || ssl,
        SmtpUsername = map.GetValueOrDefault("Smtp:Username", ""),
        SmtpFromAddress = map.GetValueOrDefault("Smtp:FromAddress", ""),
        SmtpFromName = map.GetValueOrDefault("Smtp:FromName", "Visitor Management"),
        SmtpIgnoreSslErrors = bool.TryParse(map.GetValueOrDefault("Smtp:IgnoreSslErrors", "false"), out var ise) && ise,
        SmtpPassword = null,
        SmtpPasswordConfigured = !string.IsNullOrWhiteSpace(map.GetValueOrDefault("Smtp:PasswordEncrypted")),
    };
}
