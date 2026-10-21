using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(Guid? locationId = null);
    Task<SettingsDto> GetPublicBrandingAsync();
    Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName, Guid? smtpLocationId = null);
    Task<SettingsDto> UploadLogoAsync(IFormFile file, string? userName, Guid? locationId = null);
    Task<string> GetValueAsync(string key, string fallback);
    Task<SmtpRuntimeOptions> ResolveSmtpAsync(Guid? locationId = null, CancellationToken ct = default);
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
    private static readonly Dictionary<(Guid Tenant, Guid? Loc), (SettingsDto Dto, DateTime AtUtc)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> AllowedThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiaano", "ocean", "forest", "slate", "sunrise"
    };

    private static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "inter", "sourceSans", "ibmPlex", "nunito", "poppins",
        "roboto", "openSans", "lato", "montserrat", "raleway",
        "workSans", "dmSans", "manrope", "plusJakarta", "outfit"
    };

    private static readonly HashSet<string> AllowedFontSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "small", "medium", "large"
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

    /// <summary>
    /// Tenant-default SMTP uses keys like Smtp:Host.
    /// Per-location overrides use SmtpLoc:{locationGuidN}:Host (etc.).
    /// </summary>
    internal static string SmtpStorageKey(string smtpKey, Guid? locationId) =>
        locationId is Guid id
            ? $"SmtpLoc:{id:N}:{smtpKey["Smtp:".Length..]}"
            : smtpKey;

    /// <summary>
    /// Tenant branding uses CompanyName / LogoPath / ThemePreset / FontPreset.
    /// Per-location overrides use BrandLoc:{locationGuidN}:LogoPath (etc.).
    /// </summary>
    internal static string BrandStorageKey(string brandKey, Guid? locationId) =>
        locationId is Guid id
            ? $"BrandLoc:{id:N}:{brandKey}"
            : brandKey;

    public async Task<SettingsDto> GetAsync(Guid? locationId = null)
    {
        var tenantId = EffectiveTenantId;
        lock (CacheLock)
        {
            if (Cache.TryGetValue((tenantId, locationId), out var hit) && DateTime.UtcNow - hit.AtUtc < CacheTtl)
                return CloneSettings(hit.Dto);
        }

        var rows = await _db.SystemSettings.AsNoTracking().ToListAsync();
        var tenantMap = rows
            .Where(x => !x.Key.StartsWith("SmtpLoc:", StringComparison.OrdinalIgnoreCase)
                        && !x.Key.StartsWith("BrandLoc:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        var dto = Map(tenantMap);
        ApplyBrandOverlay(dto, rows, locationId);
        ApplySmtpOverlay(dto, rows, locationId);
        ApplyConfigSmtpFallback(dto);

        lock (CacheLock)
        {
            Cache[(tenantId, locationId)] = (CloneSettings(dto), DateTime.UtcNow);
        }
        return dto;
    }

    /// <summary>Public branding for anonymous login screen — default tenant only, explicit scope.</summary>
    public async Task<SettingsDto> GetPublicBrandingAsync()
    {
        var tenantId = WellKnownTenants.TiaanoId;
        var map = await _db.SystemSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && !x.Key.StartsWith("SmtpLoc:")
                        && !x.Key.StartsWith("BrandLoc:"))
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        var dto = Map(map);
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

    public async Task<SettingsDto> UpdateAsync(SettingsDto dto, string? userName, Guid? smtpLocationId = null)
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

        // Branding can be tenant-wide or BrandLoc:{id}:* when a location is selected.
        // Other non-SMTP settings stay tenant-wide.
        string Bk(string brandKey) => BrandStorageKey(brandKey, smtpLocationId);
        await Upsert(Bk("CompanyName"), dto.CompanyName?.Trim() ?? "TIAANO");
        await Upsert(Bk("LogoPath"), string.IsNullOrWhiteSpace(dto.LogoPath) ? "/branding/tiaano-logo.png" : dto.LogoPath.Trim());
        await Upsert(Bk("ThemePreset"), NormalizeTheme(dto.ThemePreset));
        await Upsert(Bk("FontPreset"), NormalizeFont(dto.FontPreset));
        await Upsert(Bk("FontSize"), NormalizeFontSize(dto.FontSize));

        if (smtpLocationId is null)
        {
            await Upsert("VisitorIdPrefix", dto.VisitorIdPrefix);
            await Upsert("VisitorPassValidityHours", dto.VisitorPassValidityHours.ToString());
            await Upsert("ApprovalRequired", dto.ApprovalRequired.ToString().ToLowerInvariant());
            await Upsert("WalkInApprovalRequired", dto.WalkInApprovalRequired.ToString().ToLowerInvariant());
            await Upsert("PhotoRequired", dto.PhotoRequired.ToString().ToLowerInvariant());
            await Upsert("IdVerificationRequired", dto.IdVerificationRequired.ToString().ToLowerInvariant());
            await Upsert("MaxVisitDurationWarningMinutes", dto.MaxVisitDurationWarningMinutes.ToString());
            await Upsert("SessionTimeoutMinutes", dto.SessionTimeoutMinutes.ToString());
        }

        string Sk(string smtpKey) => SmtpStorageKey(smtpKey, smtpLocationId);

        await Upsert(Sk("Smtp:Enabled"), dto.SmtpEnabled.ToString().ToLowerInvariant());
        await Upsert(Sk("Smtp:Host"), (dto.SmtpHost ?? "").Trim());
        await Upsert(Sk("Smtp:Port"), Math.Clamp(dto.SmtpPort <= 0 ? 587 : dto.SmtpPort, 1, 65535).ToString());
        await Upsert(Sk("Smtp:EnableSsl"), dto.SmtpEnableSsl.ToString().ToLowerInvariant());
        await Upsert(Sk("Smtp:Username"), (dto.SmtpUsername ?? "").Trim());
        await Upsert(Sk("Smtp:FromAddress"), (dto.SmtpFromAddress ?? "").Trim());
        await Upsert(Sk("Smtp:FromName"), string.IsNullOrWhiteSpace(dto.SmtpFromName) ? "Visitor Management" : dto.SmtpFromName.Trim());
        await Upsert(Sk("Smtp:IgnoreSslErrors"), dto.SmtpIgnoreSslErrors.ToString().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(dto.SmtpPassword))
        {
            var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
            await Upsert(Sk("Smtp:PasswordEncrypted"), SensitiveDataHelper.Encrypt(dto.SmtpPassword.Trim(), key));
        }

        await _db.SaveChangesAsync();
        ClearCache(tenantId);
        return await GetAsync(smtpLocationId);
    }

    public async Task<SettingsDto> UploadLogoAsync(IFormFile file, string? userName, Guid? locationId = null)
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
        var brandingDir = locationId is Guid loc
            ? Path.Combine(webRoot, "branding", "tenants", tenantId.ToString("N"), "locations", loc.ToString("N"))
            : Path.Combine(webRoot, "branding", "tenants", tenantId.ToString("N"));
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

        var publicPath = locationId is Guid locId
            ? $"/branding/tenants/{tenantId:N}/locations/{locId:N}/{fileName}"
            : $"/branding/tenants/{tenantId:N}/{fileName}";
        var current = await GetAsync(locationId);
        current.LogoPath = publicPath;
        current.SmtpPassword = null;
        return await UpdateAsync(current, userName, locationId);
    }

    public async Task<SmtpRuntimeOptions> ResolveSmtpAsync(Guid? locationId = null, CancellationToken ct = default)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        if (_tenant.TenantId is Guid tid && tid != Guid.Empty)
        {
            var rows = await _db.SystemSettings.AsNoTracking()
                .Where(x => x.Key.StartsWith("Smtp:") || x.Key.StartsWith("SmtpLoc:"))
                .ToListAsync(ct);
            map = MergeSmtpMap(rows, locationId);
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

        var password = TryDecryptSmtpPassword(map.GetValueOrDefault("Smtp:PasswordEncrypted")) ?? "";
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

    private void ApplyBrandOverlay(SettingsDto dto, List<SystemSetting> rows, Guid? locationId)
    {
        if (locationId is not Guid id)
            return;

        var prefix = $"BrandLoc:{id:N}:";
        foreach (var row in rows)
        {
            if (!row.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = row.Key[prefix.Length..];
            if (suffix.Equals("CompanyName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(row.Value))
                dto.CompanyName = row.Value;
            else if (suffix.Equals("LogoPath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(row.Value))
                dto.LogoPath = row.Value;
            else if (suffix.Equals("ThemePreset", StringComparison.OrdinalIgnoreCase))
                dto.ThemePreset = NormalizeTheme(row.Value);
            else if (suffix.Equals("FontPreset", StringComparison.OrdinalIgnoreCase))
                dto.FontPreset = NormalizeFont(row.Value);
            else if (suffix.Equals("FontSize", StringComparison.OrdinalIgnoreCase))
                dto.FontSize = NormalizeFontSize(row.Value);
        }
    }

    private void ApplySmtpOverlay(SettingsDto dto, List<SystemSetting> rows, Guid? locationId)
    {
        var map = MergeSmtpMap(rows, locationId);
        if (map.TryGetValue("Smtp:Enabled", out var se) && bool.TryParse(se, out var enabled))
            dto.SmtpEnabled = enabled;
        if (map.TryGetValue("Smtp:Host", out var host))
            dto.SmtpHost = host ?? "";
        if (map.TryGetValue("Smtp:Port", out var port) && int.TryParse(port, out var p))
            dto.SmtpPort = p;
        if (map.TryGetValue("Smtp:EnableSsl", out var ssl) && bool.TryParse(ssl, out var enableSsl))
            dto.SmtpEnableSsl = enableSsl;
        if (map.TryGetValue("Smtp:Username", out var user))
            dto.SmtpUsername = user ?? "";
        if (map.TryGetValue("Smtp:FromAddress", out var from))
            dto.SmtpFromAddress = from ?? "";
        if (map.TryGetValue("Smtp:FromName", out var fromName) && !string.IsNullOrWhiteSpace(fromName))
            dto.SmtpFromName = fromName;
        if (map.TryGetValue("Smtp:IgnoreSslErrors", out var ise) && bool.TryParse(ise, out var ignore))
            dto.SmtpIgnoreSslErrors = ignore;
        dto.SmtpPassword = null;
        dto.SmtpPasswordConfigured = !string.IsNullOrEmpty(TryDecryptSmtpPassword(map.GetValueOrDefault("Smtp:PasswordEncrypted")));
    }

    /// <summary>
    /// Null when nothing is stored or the stored value was encrypted with a different Security:DataProtectionKey
    /// (e.g. after a server move); Settings then shows no saved password so it gets re-entered.
    /// </summary>
    private string? TryDecryptSmtpPassword(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        try
        {
            return SensitiveDataHelper.Decrypt(encrypted, SecretConfiguration.GetRequiredEncryptionKey(_config, _env));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tenant Smtp:* keys, overlaid by SmtpLoc:{id}:* when a location is selected.</summary>
    internal static Dictionary<string, string> MergeSmtpMap(IEnumerable<SystemSetting> rows, Guid? locationId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Key.StartsWith("Smtp:", StringComparison.OrdinalIgnoreCase)
                && !row.Key.StartsWith("SmtpLoc:", StringComparison.OrdinalIgnoreCase))
                map[row.Key] = row.Value;
        }

        if (locationId is Guid id)
        {
            var prefix = $"SmtpLoc:{id:N}:";
            foreach (var row in rows)
            {
                if (!row.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var suffix = row.Key[prefix.Length..];
                map[$"Smtp:{suffix}"] = row.Value;
            }
        }

        return map;
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
            var keys = Cache.Keys.Where(k => k.Tenant == tenantId).ToList();
            foreach (var k in keys)
                Cache.Remove(k);
        }
    }

    private static SettingsDto CloneSettings(SettingsDto s) => new()
    {
        CompanyName = s.CompanyName,
        LogoPath = s.LogoPath,
        ThemePreset = s.ThemePreset,
        FontPreset = s.FontPreset,
        FontSize = s.FontSize,
        VisitorIdPrefix = s.VisitorIdPrefix,
        VisitorPassValidityHours = s.VisitorPassValidityHours,
        ApprovalRequired = s.ApprovalRequired,
        WalkInApprovalRequired = s.WalkInApprovalRequired,
        PhotoRequired = s.PhotoRequired,
        IdVerificationRequired = s.IdVerificationRequired,
        MaxVisitDurationWarningMinutes = s.MaxVisitDurationWarningMinutes,
        SessionTimeoutMinutes = s.SessionTimeoutMinutes,
        SmtpEnabled = s.SmtpEnabled,
        SmtpHost = s.SmtpHost,
        SmtpPort = s.SmtpPort,
        SmtpEnableSsl = s.SmtpEnableSsl,
        SmtpUsername = s.SmtpUsername,
        SmtpFromAddress = s.SmtpFromAddress,
        SmtpFromName = s.SmtpFromName,
        SmtpIgnoreSslErrors = s.SmtpIgnoreSslErrors,
        SmtpPassword = null,
        SmtpPasswordConfigured = s.SmtpPasswordConfigured,
    };

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

    private static string NormalizeFontSize(string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "medium" : value.Trim().ToLowerInvariant();
        return AllowedFontSizes.Contains(key) ? key : "medium";
    }

    private static SettingsDto Map(Dictionary<string, string> map) => new()
    {
        CompanyName = map.GetValueOrDefault("CompanyName", "TIAANO"),
        LogoPath = map.GetValueOrDefault("LogoPath", "/branding/tiaano-logo.png"),
        ThemePreset = NormalizeTheme(map.GetValueOrDefault("ThemePreset", "tiaano")),
        FontPreset = NormalizeFont(map.GetValueOrDefault("FontPreset", "inter")),
        FontSize = NormalizeFontSize(map.GetValueOrDefault("FontSize", "medium")),
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
