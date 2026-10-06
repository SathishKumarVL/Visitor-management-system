using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface IIntegrationKeyService
{
    Task<IReadOnlyList<IntegrationApiKeyDto>> ListAsync();
    Task<CreatedIntegrationApiKeyDto> CreateAsync(CreateIntegrationApiKeyRequest request, string? user);
    Task RevokeAsync(Guid id, string? user);
}

public sealed class IntegrationKeyService : IIntegrationKeyService
{
    private const string KeyPrefix = "vms_";

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public IntegrationKeyService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static IReadOnlyList<string> ParseScopes(string? scopes) =>
        (scopes ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => IntegrationScopes.All.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToList();

    public async Task<IReadOnlyList<IntegrationApiKeyDto>> ListAsync()
    {
        var keys = await _db.IntegrationApiKeys.AsNoTracking()
            .OrderByDescending(k => k.IsActive).ThenByDescending(k => k.CreatedAt)
            .ToListAsync();
        return keys.Select(Map).ToList();
    }

    public async Task<CreatedIntegrationApiKeyDto> CreateAsync(CreateIntegrationApiKeyRequest request, string? user)
    {
        var tenantId = _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Name is required.");
        var scopes = ParseScopes(string.Join(',', request.Scopes ?? []));
        if (scopes.Count == 0)
            throw new InvalidOperationException("Choose at least one permission (reports or employees).");

        var plain = KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var entity = new IntegrationApiKey
        {
            TenantId = tenantId,
            Name = name,
            KeyPrefix = plain[..12],
            KeyHash = HashKey(plain),
            Scopes = string.Join(',', scopes),
            CreatedBy = user,
        };
        _db.IntegrationApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        return new CreatedIntegrationApiKeyDto { Key = Map(entity), PlainKey = plain };
    }

    public async Task RevokeAsync(Guid id, string? user)
    {
        var key = await _db.IntegrationApiKeys.FirstOrDefaultAsync(k => k.Id == id)
                  ?? throw new InvalidOperationException("API key not found.");
        if (!key.IsActive) return;
        key.IsActive = false;
        key.RevokedAt = DateTime.UtcNow;
        key.RevokedBy = user;
        await _db.SaveChangesAsync();
    }

    private static IntegrationApiKeyDto Map(IntegrationApiKey k) => new()
    {
        Id = k.Id,
        Name = k.Name,
        KeyPrefix = k.KeyPrefix,
        Scopes = ParseScopes(k.Scopes).ToList(),
        IsActive = k.IsActive,
        CreatedAt = VisitorService.AsUtc(k.CreatedAt)!.Value,
        CreatedBy = k.CreatedBy,
        LastUsedAt = VisitorService.AsUtc(k.LastUsedAt),
        RevokedAt = VisitorService.AsUtc(k.RevokedAt),
    };

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
