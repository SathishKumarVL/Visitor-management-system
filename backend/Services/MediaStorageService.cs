using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;

namespace Tiaano.Vms.Api.Services;

public interface IMediaStorageService
{
    string PrivateRoot { get; }
    string LegacyUploadsRoot { get; }
    Task<string> SaveVisitorPhotoAsync(Guid visitorId, byte[] bytes, string? contentTypeHint = null);
    Task<(Stream Stream, string ContentType)?> OpenAsync(string fileName);
    string ToPublicApiPath(string storedFileName);
    bool IsAllowedImage(byte[] bytes, out string contentType);
}

public sealed class MediaStorageService : IMediaStorageService
{
    private readonly IWebHostEnvironment _env;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public MediaStorageService(IWebHostEnvironment env)
    {
        _env = env;
        Directory.CreateDirectory(PrivateRoot);
        Directory.CreateDirectory(LegacyUploadsRoot);
    }

    public string PrivateRoot =>
        Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "media", "visitors"));

    public string LegacyUploadsRoot =>
        Path.GetFullPath(Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads"));

    public string ToPublicApiPath(string storedFileName) =>
        $"/api/media/{Uri.EscapeDataString(Path.GetFileName(storedFileName))}";

    public bool IsAllowedImage(byte[] bytes, out string contentType)
    {
        contentType = "application/octet-stream";
        if (bytes.Length < 12 || bytes.Length > 5_000_000) return false;

        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            contentType = "image/png";
            return true;
        }

        // WebP (RIFF....WEBP)
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

        var ext = detectedType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        var safeName = $"{visitorId:N}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}{ext}";
        var fullPath = Path.Combine(PrivateRoot, safeName);
        // Path traversal guard
        if (!Path.GetFullPath(fullPath).StartsWith(PrivateRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid media path.");

        await File.WriteAllBytesAsync(fullPath, bytes);
        return safeName;
    }

    public Task<(Stream Stream, string ContentType)?> OpenAsync(string fileName)
    {
        var safe = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safe) || safe != (fileName?.Trim() ?? string.Empty))
            return Task.FromResult<(Stream, string)?>(null);

        var privatePath = Path.Combine(PrivateRoot, safe);
        var legacyPath = Path.Combine(LegacyUploadsRoot, safe);

        string? chosen = null;
        if (File.Exists(privatePath) && Path.GetFullPath(privatePath).StartsWith(PrivateRoot, StringComparison.OrdinalIgnoreCase))
            chosen = privatePath;
        else if (File.Exists(legacyPath) && Path.GetFullPath(legacyPath).StartsWith(LegacyUploadsRoot, StringComparison.OrdinalIgnoreCase))
            chosen = legacyPath;

        if (chosen is null) return Task.FromResult<(Stream, string)?>(null);

        if (!ContentTypes.TryGetContentType(chosen, out var contentType))
            contentType = "application/octet-stream";

        Stream stream = new FileStream(chosen, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<(Stream, string)?>((stream, contentType));
    }
}
