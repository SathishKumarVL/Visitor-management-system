using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface IMediaStorageService
{
    string PrivateRoot { get; }
    Task<string> SaveVisitorPhotoAsync(Guid visitorId, byte[] bytes, string? contentTypeHint = null);
    Task<(Stream Stream, string ContentType)?> OpenAsync(string fileName);
    string ToPublicApiPath(string storedFileName);
    bool IsAllowedImage(byte[] bytes, out string contentType);
}

public sealed class MediaStorageService : IMediaStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenant;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public MediaStorageService(IWebHostEnvironment env, ITenantContext tenant)
    {
        _env = env;
        _tenant = tenant;
        if (_tenant.TenantId is Guid id && id != Guid.Empty)
            Directory.CreateDirectory(PrivateRoot);
    }

    private Guid EffectiveTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required for media access.");

    public string PrivateRoot =>
        Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "media", "tenants", EffectiveTenantId.ToString("N"), "visitors"));

    public string ToPublicApiPath(string storedFileName) =>
        $"/api/media/{Uri.EscapeDataString(Path.GetFileName(storedFileName))}";

    public bool IsAllowedImage(byte[] bytes, out string contentType)
    {
        contentType = "application/octet-stream";
        if (bytes.Length < 12 || bytes.Length > 5_000_000) return false;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            contentType = "image/png";
            return true;
        }

        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            contentType = "image/webp";
            return true;
        }

        return false;
    }

    public async Task<string> SaveVisitorPhotoAsync(Guid visitorId, byte[] bytes, string? contentTypeHint = null)
    {
        if (!IsAllowedImage(bytes, out var detectedType))
            throw new InvalidOperationException("Photo must be a valid JPEG, PNG, or WebP image under 5MB.");

        Directory.CreateDirectory(PrivateRoot);

        var ext = detectedType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        var safeName = $"{visitorId:N}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}{ext}";
        var fullPath = Path.Combine(PrivateRoot, safeName);
        if (!Path.GetFullPath(fullPath).StartsWith(PrivateRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid media path.");

        await File.WriteAllBytesAsync(fullPath, bytes);
        return safeName;
    }

    /// <summary>
    /// Opens media only from the current tenant's private root.
    /// Shared/legacy roots are not used — prevents cross-tenant file reads by filename collision.
    /// </summary>
    public Task<(Stream Stream, string ContentType)?> OpenAsync(string fileName)
    {
        if (_tenant.TenantId is not Guid || _tenant.TenantId == Guid.Empty)
            return Task.FromResult<(Stream, string)?>(null);

        var trimmed = fileName?.Trim() ?? string.Empty;
        var safe = Path.GetFileName(trimmed);
        // Reject path traversal / nested paths; filename must equal the provided value after GetFileName.
        if (string.IsNullOrWhiteSpace(safe) || !string.Equals(safe, trimmed, StringComparison.Ordinal))
            return Task.FromResult<(Stream, string)?>(null);

        if (safe.Contains("..", StringComparison.Ordinal) || safe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Task.FromResult<(Stream, string)?>(null);

        var privatePath = Path.Combine(PrivateRoot, safe);
        var full = Path.GetFullPath(privatePath);
        if (!full.StartsWith(PrivateRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return Task.FromResult<(Stream, string)?>(null);

        if (!ContentTypes.TryGetContentType(full, out var contentType))
            contentType = "application/octet-stream";

        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<(Stream, string)?>((stream, contentType));
    }
}
