using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services.Face;

namespace Tiaano.Vms.Api.Services;

/// <summary>The registered face that matched a face sign-in attempt.</summary>
public sealed record FaceLoginMatch(Guid FaceId, string UserId, string PersonName, double Similarity);

public interface IUserFaceLoginService
{
    /// <summary>True when the server can recognise faces and at least one login has a face registered.</summary>
    Task<bool> IsAvailableAsync();

    Task<IReadOnlyList<UserFaceLoginDto>> ListAsync(string userId);
    Task<UserFaceLoginDto> AddAsync(string userId, AddUserFaceLoginRequest request, bool actorIsSuperAdmin, string? actor);
    Task RemoveAsync(string userId, Guid faceId, bool actorIsSuperAdmin);

    /// <summary>Finds the login whose registered face matches the photo, or null when none is close enough.</summary>
    Task<FaceLoginMatch?> MatchAsync(string photoBase64);

    Task MarkUsedAsync(Guid faceId);
}

public class UserFaceLoginService : IUserFaceLoginService
{
    public const int MaxFacesPerUser = 20;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFaceEmbeddingService _faces;
    private readonly FaceRecognitionOptions _options;

    public UserFaceLoginService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IFaceEmbeddingService faces,
        IOptions<FaceRecognitionOptions> options)
    {
        _db = db;
        _userManager = userManager;
        _faces = faces;
        _options = options.Value;
    }

    private double LoginThreshold => Math.Max(_faces.MatchThreshold, _options.LoginMatchThreshold);

    public async Task<bool> IsAvailableAsync() =>
        _faces.IsAvailable
        && await _db.UserFaceLogins.IgnoreQueryFilters()
            .AnyAsync(f => f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions);

    public async Task<IReadOnlyList<UserFaceLoginDto>> ListAsync(string userId)
    {
        await FindUserAsync(userId);
        return await _db.UserFaceLogins.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.PersonName).ThenBy(f => f.CreatedAt)
            .Select(f => new UserFaceLoginDto
            {
                Id = f.Id,
                PersonName = f.PersonName,
                CreatedAt = f.CreatedAt,
                CreatedBy = f.CreatedBy,
                LastUsedAt = f.LastUsedAt
            })
            .ToListAsync();
    }

    public async Task<UserFaceLoginDto> AddAsync(
        string userId, AddUserFaceLoginRequest request, bool actorIsSuperAdmin, string? actor)
    {
        var user = await FindUserAsync(userId);
        await EnsureMayManageAsync(user, actorIsSuperAdmin);

        var personName = (request.PersonName ?? string.Empty).Trim();
        if (personName.Length == 0)
            throw new InvalidOperationException("Enter the name of the person whose face this is.");
        if (personName.Length > 100)
            throw new InvalidOperationException("Person name must be 100 characters or fewer.");

        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face recognition is not available on this server.");

        var count = await _db.UserFaceLogins.CountAsync(f => f.UserId == userId);
        if (count >= MaxFacesPerUser)
            throw new InvalidOperationException(
                $"A login can have at most {MaxFacesPerUser} faces. Remove one before adding another.");

        var probe = Embed(request.PhotoBase64);

        // Face sign-in picks the login from the face alone, so one person must never open two logins.
        var clash = await BestMatchAsync(probe, excludeUserId: userId);
        if (clash is not null && clash.Similarity >= LoginThreshold)
        {
            var owner = await _userManager.Users.IgnoreQueryFilters()
                .Where(u => u.Id == clash.UserId)
                .Select(u => new { u.UserName, u.TenantId })
                .FirstOrDefaultAsync();
            throw new InvalidOperationException(owner is not null && owner.TenantId == user.TenantId
                ? $"This face is already registered for the login '{owner.UserName}' (as {clash.PersonName}). Remove it there first; one person can sign in to only one login with their face."
                : "This face is already registered for another login. One person can sign in to only one login with their face.");
        }

        var entity = new UserFaceLogin
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            PersonName = personName,
            Descriptor = ToBytes(probe),
            Dimensions = probe.Length,
            Model = _faces.ModelId,
            CreatedBy = actor
        };
        _db.UserFaceLogins.Add(entity);
        await _db.SaveChangesAsync();

        return new UserFaceLoginDto
        {
            Id = entity.Id,
            PersonName = entity.PersonName,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy
        };
    }

    public async Task RemoveAsync(string userId, Guid faceId, bool actorIsSuperAdmin)
    {
        var user = await FindUserAsync(userId);
        await EnsureMayManageAsync(user, actorIsSuperAdmin);

        var face = await _db.UserFaceLogins.FirstOrDefaultAsync(f => f.Id == faceId && f.UserId == userId)
            ?? throw new InvalidOperationException("Face not found.");
        _db.UserFaceLogins.Remove(face);
        await _db.SaveChangesAsync();
    }

    public async Task<FaceLoginMatch?> MatchAsync(string photoBase64)
    {
        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face sign-in is not available on this server.");

        var probe = Embed(photoBase64);
        var best = await BestMatchAsync(probe, excludeUserId: null);
        if (best is null || best.Similarity < LoginThreshold) return null;

        var runnerUp = await BestMatchAsync(probe, excludeUserId: best.UserId);
        if (runnerUp is not null && best.Similarity - runnerUp.Similarity < _options.LoginAmbiguityMargin)
            return null;

        return best;
    }

    public async Task MarkUsedAsync(Guid faceId) =>
        await _db.UserFaceLogins.IgnoreQueryFilters()
            .Where(f => f.Id == faceId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.LastUsedAt, DateTime.UtcNow));

    /// <summary>
    /// Searches every tenant: sign-in happens before any tenant is known, exactly like username lookup.
    /// </summary>
    private async Task<FaceLoginMatch?> BestMatchAsync(float[] probe, string? excludeUserId)
    {
        var candidates = _db.UserFaceLogins.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions)
            .Where(f => excludeUserId == null || f.UserId != excludeUserId)
            .Select(f => new { f.Id, f.UserId, f.PersonName, f.Descriptor })
            .AsAsyncEnumerable();

        FaceLoginMatch? best = null;
        await foreach (var candidate in candidates)
        {
            var stored = FromBytes(candidate.Descriptor);
            if (stored.Length != probe.Length) continue;
            var similarity = _faces.Similarity(probe, stored);
            if (best is null || similarity > best.Similarity)
                best = new FaceLoginMatch(candidate.Id, candidate.UserId, candidate.PersonName, similarity);
        }
        return best;
    }

    private async Task<ApplicationUser> FindUserAsync(string userId) =>
        await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId)
        ?? throw new InvalidOperationException("User not found.");

    private async Task EnsureMayManageAsync(ApplicationUser user, bool actorIsSuperAdmin)
    {
        if (!actorIsSuperAdmin && await _userManager.IsInRoleAsync(user, AppRoles.SuperAdmin))
            throw new UnauthorizedAccessException("Only a Super Admin can change face sign-in for a Super Admin.");
    }

    private float[] Embed(string? photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            throw new InvalidOperationException("A face capture is required.");

        var raw = photoBase64.Contains(',') ? photoBase64.Split(',')[1] : photoBase64;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Photo data is invalid.");
        }

        try
        {
            return _faces.Embed(bytes).Vector;
        }
        catch (FaceEmbeddingException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
