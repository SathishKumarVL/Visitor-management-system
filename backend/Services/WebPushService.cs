using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using static Tiaano.Vms.Api.Services.WebPushProtocol;

namespace Tiaano.Vms.Api.Services;

public sealed record PushMessage(string Title, string Body, string? Link = null, string? Tag = null);

public sealed record WebPushSubscriptionInput(string Endpoint, string P256dh, string Auth);

/// <summary>
/// Browser / phone push (Web Push with VAPID). Each tenant gets its own VAPID key pair, generated on first use
/// and kept in SystemSettings (private key encrypted), unless Push:VapidPublicKey / Push:VapidPrivateKey are configured.
/// </summary>
public interface IWebPushService
{
    Task<string> GetPublicKeyAsync(Guid tenantId);
    Task SubscribeAsync(Guid tenantId, string userId, WebPushSubscriptionInput subscription);
    Task UnsubscribeAsync(Guid tenantId, string userId, string endpoint);
    Task<int> CountSubscriptionsAsync(Guid tenantId, string userId);
    /// <summary>Sends to every browser the user enabled; returns how many accepted it. Expired subscriptions are removed.</summary>
    Task<int> SendToUserAsync(Guid tenantId, string userId, PushMessage message, CancellationToken ct = default);
}

public sealed class WebPushService : IWebPushService
{
    public const string WebPlatform = "web";
    private const string PublicKeySetting = "Push:VapidPublicKey";
    private const string PrivateKeySetting = "Push:VapidPrivateKeyEncrypted";

    private static readonly ConcurrentDictionary<Guid, VapidKeys> KeyCache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<WebPushService> _logger;

    public WebPushService(ApplicationDbContext db, IConfiguration config, IHostEnvironment env, ILogger<WebPushService> logger)
    {
        _db = db;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task<string> GetPublicKeyAsync(Guid tenantId) => (await GetKeysAsync(tenantId)).PublicKey;

    public async Task SubscribeAsync(Guid tenantId, string userId, WebPushSubscriptionInput subscription)
    {
        var endpoint = subscription.Endpoint?.Trim() ?? "";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The browser sent an invalid push subscription.");
        if (endpoint.Length > 500)
            throw new InvalidOperationException("The browser's push address is too long to store.");
        if (!IsValidKey(subscription.P256dh, k => k.Length == 65 && k[0] == 0x04)
            || !IsValidKey(subscription.Auth, k => k.Length >= 16))
            throw new InvalidOperationException("The browser sent an incomplete push subscription.");

        var row = await _db.DevicePushTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Token == endpoint);
        if (row is null)
        {
            row = new DevicePushToken { TenantId = tenantId, Token = endpoint };
            _db.DevicePushTokens.Add(row);
        }
        else
        {
            row.UpdatedAt = DateTime.UtcNow;
        }

        // A shared browser follows whoever signed in last.
        row.UserId = userId;
        row.Platform = WebPlatform;
        row.P256dh = subscription.P256dh.Trim();
        row.Auth = subscription.Auth.Trim();
        await _db.SaveChangesAsync();
    }

    public async Task UnsubscribeAsync(Guid tenantId, string userId, string endpoint)
    {
        var trimmed = endpoint?.Trim() ?? "";
        var rows = await _db.DevicePushTokens.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.Token == trimmed)
            .ToListAsync();
        if (rows.Count == 0) return;
        _db.DevicePushTokens.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    public Task<int> CountSubscriptionsAsync(Guid tenantId, string userId) =>
        _db.DevicePushTokens.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == tenantId && t.UserId == userId && t.Platform == WebPlatform
                && t.P256dh != null && t.Auth != null);

    public async Task<int> SendToUserAsync(Guid tenantId, string userId, PushMessage message, CancellationToken ct = default)
    {
        var subscriptions = await _db.DevicePushTokens.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.Platform == WebPlatform
                && t.P256dh != null && t.Auth != null)
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return 0;

        var keys = await GetKeysAsync(tenantId);
        var subject = Subject();
        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = string.IsNullOrWhiteSpace(message.Link) ? "/" : message.Link,
            tag = message.Tag
        }, Json);

        var delivered = 0;
        var expired = new List<DevicePushToken>();
        Exception? lastError = null;
        foreach (var sub in subscriptions)
        {
            try
            {
                using var request = CreateRequest(sub.Token, sub.P256dh!, sub.Auth!, payload, keys, subject);
                using var response = await Http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    delivered++;
                }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // The browser dropped the subscription (signed out, cleared data, uninstalled).
                    expired.Add(sub);
                }
                else
                {
                    var detail = await response.Content.ReadAsStringAsync(ct);
                    throw new InvalidOperationException(
                        $"Push service answered {(int)response.StatusCode} {response.ReasonPhrase}: {(detail.Length > 200 ? detail[..200] : detail)}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Push to a device of user {UserId} failed", userId);
            }
        }

        if (expired.Count > 0)
        {
            _db.DevicePushTokens.RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
        }

        if (delivered == 0 && lastError is not null)
            throw new InvalidOperationException($"Push failed: {lastError.GetBaseException().Message}", lastError);
        return delivered;
    }

    private static bool IsValidKey(string? value, Func<byte[], bool> check)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            return check(Base64UrlDecode(value));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string Subject()
    {
        var configured = _config["Push:Subject"]?.Trim();
        if (!string.IsNullOrEmpty(configured)) return configured;
        var baseUrl = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        // Push services require an https URL or mailto: contact.
        return !string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : "mailto:noreply@tiaano.com";
    }

    private async Task<VapidKeys> GetKeysAsync(Guid tenantId)
    {
        var configuredPublic = _config["Push:VapidPublicKey"]?.Trim();
        var configuredPrivate = _config["Push:VapidPrivateKey"]?.Trim();
        if (!string.IsNullOrEmpty(configuredPublic) && !string.IsNullOrEmpty(configuredPrivate))
            return new VapidKeys(configuredPublic, configuredPrivate);

        if (KeyCache.TryGetValue(tenantId, out var cached)) return cached;

        var encryptionKey = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
        var stored = await ReadStoredKeysAsync(tenantId, encryptionKey);
        if (stored is null)
        {
            var generated = GenerateVapidKeys();
            _db.SystemSettings.Add(new SystemSetting
            {
                TenantId = tenantId,
                Key = PublicKeySetting,
                Value = generated.PublicKey,
                Description = "Web push public key (VAPID)"
            });
            _db.SystemSettings.Add(new SystemSetting
            {
                TenantId = tenantId,
                Key = PrivateKeySetting,
                Value = SensitiveDataHelper.Encrypt(generated.PrivateKey, encryptionKey),
                Description = "Web push private key (VAPID, encrypted)"
            });
            try
            {
                await _db.SaveChangesAsync();
                stored = generated;
            }
            catch (DbUpdateException)
            {
                // Another request generated the pair first; use theirs.
                foreach (var entry in _db.ChangeTracker.Entries<SystemSetting>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                stored = await ReadStoredKeysAsync(tenantId, encryptionKey)
                    ?? throw new InvalidOperationException("Could not create the push notification keys.");
            }
        }

        KeyCache[tenantId] = stored;
        return stored;
    }

    private async Task<VapidKeys?> ReadStoredKeysAsync(Guid tenantId, string encryptionKey)
    {
        var map = await _db.SystemSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && (s.Key == PublicKeySetting || s.Key == PrivateKeySetting))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        if (!map.TryGetValue(PublicKeySetting, out var publicKey) || !map.TryGetValue(PrivateKeySetting, out var encrypted))
            return null;
        try
        {
            return new VapidKeys(publicKey, SensitiveDataHelper.Decrypt(encrypted, encryptionKey));
        }
        catch (Exception ex)
        {
            // Encrypted with a different Security:DataProtectionKey (e.g. a server move): browsers must re-enable push.
            _logger.LogWarning(ex, "Stored push keys for tenant {TenantId} could not be decrypted; creating new ones", tenantId);
            var stale = await _db.SystemSettings.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && (s.Key == PublicKeySetting || s.Key == PrivateKeySetting))
                .ToListAsync();
            _db.SystemSettings.RemoveRange(stale);
            var subscriptions = await _db.DevicePushTokens.IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && t.Platform == WebPlatform)
                .ToListAsync();
            _db.DevicePushTokens.RemoveRange(subscriptions);
            await _db.SaveChangesAsync();
            return null;
        }
    }
}
