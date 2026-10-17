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

/// <summary>Face template for an Employees master record, plus the decoded photo it came from.</summary>
public sealed record EmployeeFaceTemplate(byte[] Descriptor, int Dimensions, string Model, byte[] PhotoBytes);

public interface IUserFaceLoginService
{
    /// <summary>True when the server can recognise faces, so the login screen offers face sign-in.</summary>
    Task<bool> IsAvailableAsync();

    Task<IReadOnlyList<UserFaceLoginDto>> ListAsync(string userId);
    Task<UserFaceLoginDto> AddAsync(string userId, AddUserFaceLoginRequest request, bool actorIsSuperAdmin, string? actor);
    Task RemoveAsync(string userId, Guid faceId, bool actorIsSuperAdmin);

    /// <summary>Finds the login whose registered face matches the photo, or null when none is close enough.</summary>
    Task<FaceLoginMatch?> MatchAsync(string photoBase64);

    /// <summary>
    /// Recognises a group employee, at any location of the caller's tenant, from the faces registered on logins
    /// and the faces enrolled on Employees master records.
    /// </summary>
    Task<GroupEmployeeMatchDto?> MatchGroupEmployeeAsync(string photoBase64);

    /// <summary>
    /// Builds the face template for an Employees master record, rejecting a face already enrolled on another
    /// active employee of the tenant or registered for face sign-in on someone else's login.
    /// </summary>
    Task<EmployeeFaceTemplate> BuildEmployeeFaceAsync(string photoBase64, Guid? employeeId, string? employeeUserId);

    /// <summary>Registers the employee's enrolled face for face sign-in on their new login. False when none is enrolled.</summary>
    Task<bool> CopyEmployeeFaceToLoginAsync(Guid employeeId, string userId, string? actor);

    /// <summary>After the employee's face is recaptured, makes it the only face that signs in to their login.</summary>
    Task ReplaceLoginFaceFromEmployeeAsync(Guid employeeId, string userId, string? actor);

    Task MarkUsedAsync(Guid faceId);
}

public class UserFaceLoginService : IUserFaceLoginService
{
    public const int MaxFacesPerUser = 20;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFaceEmbeddingService _faces;
    private readonly FaceRecognitionOptions _options;
    private readonly ITenantContext _tenant;

    public UserFaceLoginService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IFaceEmbeddingService faces,
        IOptions<FaceRecognitionOptions> options,
        ITenantContext tenant)
    {
        _db = db;
        _userManager = userManager;
        _faces = faces;
        _options = options.Value;
        _tenant = tenant;
    }

    private double LoginThreshold => Math.Max(_faces.MatchThreshold, _options.LoginMatchThreshold);

    /// <summary>
    /// Looser than <see cref="LoginThreshold"/>: a second capture of an enrolled person often scores
    /// below the sign-in bar, and it must still count as the same face.
    /// </summary>
    private double DuplicateThreshold => _faces.MatchThreshold;

    public Task<bool> IsAvailableAsync() => Task.FromResult(_faces.IsAvailable);

    public async Task<IReadOnlyList<UserFaceLoginDto>> ListAsync(string userId)
    {
        EnsureInActorLocation(await FindUserAsync(userId));
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

        var duplicate = await BestMatchAsync(probe, onlyUserId: userId);
        if (duplicate is not null && duplicate.Similarity >= DuplicateThreshold)
            throw new InvalidOperationException(
                $"This face is already registered for this login (as {duplicate.PersonName}). Each person needs to be registered only once.");

        // Face sign-in picks the login from the face alone, so one person must never open two logins.
        var clash = await BestMatchAsync(probe, excludeUserId: userId);
        if (clash is not null && clash.Similarity >= DuplicateThreshold)
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
        var best = await BestMatchAsync(probe);
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

    public async Task<GroupEmployeeMatchDto?> MatchGroupEmployeeAsync(string photoBase64)
    {
        if (_tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant context is required.");
        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face recognition is not configured on this server.");

        var probe = Embed(photoBase64);

        // One score per person: a login face and the employee-record face of the same employee are one identity,
        // so having both never makes the match ambiguous.
        var people = new Dictionary<string, GroupCandidate>(StringComparer.Ordinal);
        void Consider(string key, GroupCandidate candidate)
        {
            if (!people.TryGetValue(key, out var current))
            {
                people[key] = candidate;
                return;
            }
            var winner = candidate.Similarity > current.Similarity ? candidate : current;
            people[key] = winner with
            {
                UserId = winner.UserId ?? current.UserId ?? candidate.UserId,
                EmployeeId = winner.EmployeeId ?? current.EmployeeId ?? candidate.EmployeeId
            };
        }

        var loginFaces = _db.UserFaceLogins.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions)
            .Select(f => new { f.UserId, f.PersonName, f.Descriptor })
            .AsAsyncEnumerable();
        await foreach (var face in loginFaces)
        {
            var stored = FromBytes(face.Descriptor);
            if (stored.Length != probe.Length) continue;
            Consider(face.UserId, new GroupCandidate(face.UserId, null, face.PersonName, _faces.Similarity(probe, stored)));
        }

        var employeeFaces = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && e.FaceDescriptor != null
                && e.FaceModel == _faces.ModelId && e.FaceDimensions == _faces.Dimensions)
            .Select(e => new { e.Id, e.UserId, e.FullName, e.FaceDescriptor })
            .AsAsyncEnumerable();
        await foreach (var face in employeeFaces)
        {
            var stored = FromBytes(face.FaceDescriptor!);
            if (stored.Length != probe.Length) continue;
            Consider(face.UserId ?? $"employee:{face.Id}",
                new GroupCandidate(face.UserId, face.Id, face.FullName, _faces.Similarity(probe, stored)));
        }

        var ranked = people.Values.OrderByDescending(p => p.Similarity).Take(2).ToList();
        if (ranked.Count == 0 || ranked[0].Similarity < LoginThreshold) return null;
        if (ranked.Count > 1 && ranked[0].Similarity - ranked[1].Similarity < _options.LoginAmbiguityMargin)
            return null;
        var best = ranked[0];

        var user = best.UserId is null
            ? null
            : await _userManager.Users.AsNoTracking()
                .Where(u => u.Id == best.UserId && u.TenantId == tenantId && u.IsActive)
                .Select(u => new { u.Id, u.Email, LocationName = u.Location != null ? u.Location.Name : null })
                .FirstOrDefaultAsync();

        var matchedUserId = user?.Id;
        var employeeQuery = _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId && e.IsActive);
        employeeQuery = best.EmployeeId is Guid employeeId
            ? employeeQuery.Where(e => e.Id == employeeId)
            : employeeQuery.Where(e => matchedUserId != null && e.UserId == matchedUserId);
        var employee = await employeeQuery
            .Select(e => new
            {
                e.Id,
                e.FullName,
                e.Gender,
                e.Email,
                e.Phone,
                e.Code,
                e.Designation,
                DepartmentName = e.Department.Name,
                LocationName = e.Location != null ? e.Location.Name : null
            })
            .FirstOrDefaultAsync();
        if (user is null && employee is null) return null;

        var companyName = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync() ?? string.Empty;

        return new GroupEmployeeMatchDto
        {
            UserId = user?.Id,
            EmployeeId = employee?.Id,
            // A shared login (e.g. one guard login) registers each person under their own name.
            FullName = best.PersonName,
            Gender = employee?.Gender,
            Email = string.IsNullOrWhiteSpace(employee?.Email) ? user?.Email : employee.Email,
            Phone = employee?.Phone,
            EmployeeCode = employee?.Code,
            Designation = employee?.Designation,
            DepartmentName = employee?.DepartmentName,
            LocationName = employee?.LocationName ?? user?.LocationName,
            CompanyName = companyName,
            Similarity = Math.Round(best.Similarity, 4)
        };
    }

    private sealed record GroupCandidate(string? UserId, Guid? EmployeeId, string PersonName, double Similarity);

    public async Task<EmployeeFaceTemplate> BuildEmployeeFaceAsync(string photoBase64, Guid? employeeId, string? employeeUserId)
    {
        if (_tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Tenant context is required.");
        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face recognition is not available on this server.");

        var bytes = DecodePhoto(photoBase64);
        var probe = EmbedBytes(bytes);

        var others = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && e.FaceDescriptor != null
                && e.FaceModel == _faces.ModelId && e.FaceDimensions == _faces.Dimensions)
            .Where(e => employeeId == null || e.Id != employeeId)
            .Select(e => new { e.FullName, e.Code, e.FaceDescriptor })
            .AsAsyncEnumerable();
        await foreach (var other in others)
        {
            var stored = FromBytes(other.FaceDescriptor!);
            if (stored.Length != probe.Length || _faces.Similarity(probe, stored) < DuplicateThreshold) continue;
            var who = string.IsNullOrWhiteSpace(other.Code) ? other.FullName : $"{other.FullName} ({other.Code})";
            throw new InvalidOperationException(
                $"This face is already enrolled for the employee {who}. One person can have only one employee record.");
        }

        var clash = await BestMatchAsync(probe, excludeUserId: employeeUserId);
        if (clash is not null && clash.Similarity >= DuplicateThreshold)
        {
            var owner = await _userManager.Users.IgnoreQueryFilters()
                .Where(u => u.Id == clash.UserId)
                .Select(u => new { u.UserName, u.TenantId })
                .FirstOrDefaultAsync();
            throw new InvalidOperationException(owner is not null && owner.TenantId == tenantId
                ? $"This face already signs in to the login '{owner.UserName}' (as {clash.PersonName}). Remove it there first."
                : "This face is already registered for another login.");
        }

        return new EmployeeFaceTemplate(ToBytes(probe), probe.Length, _faces.ModelId, bytes);
    }

    public async Task ReplaceLoginFaceFromEmployeeAsync(Guid employeeId, string userId, string? actor)
    {
        await _db.UserFaceLogins.Where(f => f.UserId == userId).ExecuteDeleteAsync();
        await CopyEmployeeFaceToLoginAsync(employeeId, userId, actor);
    }

    public async Task<bool> CopyEmployeeFaceToLoginAsync(Guid employeeId, string userId, string? actor)
    {
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.TenantId, e.FullName, e.FaceDescriptor, e.FaceModel, e.FaceDimensions })
            .FirstOrDefaultAsync();
        if (employee?.FaceDescriptor is null
            || employee.FaceModel != _faces.ModelId
            || employee.FaceDimensions != _faces.Dimensions)
            return false;

        var clash = await BestMatchAsync(FromBytes(employee.FaceDescriptor), excludeUserId: userId);
        if (clash is not null && clash.Similarity >= DuplicateThreshold) return false;

        var personName = employee.FullName.Length > 100 ? employee.FullName[..100] : employee.FullName;
        _db.UserFaceLogins.Add(new UserFaceLogin
        {
            TenantId = employee.TenantId,
            UserId = userId,
            PersonName = personName,
            Descriptor = employee.FaceDescriptor,
            Dimensions = employee.FaceDimensions.Value,
            Model = employee.FaceModel,
            CreatedBy = actor
        });
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Searches every tenant unless <paramref name="tenantId"/> is given: sign-in happens before any tenant is
    /// known, exactly like username lookup.
    /// </summary>
    private async Task<FaceLoginMatch?> BestMatchAsync(
        float[] probe, string? excludeUserId = null, string? onlyUserId = null, Guid? tenantId = null)
    {
        var candidates = _db.UserFaceLogins.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions)
            .Where(f => excludeUserId == null || f.UserId != excludeUserId)
            .Where(f => onlyUserId == null || f.UserId == onlyUserId)
            .Where(f => tenantId == null || f.TenantId == tenantId)
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
        EnsureInActorLocation(user);
    }

    private void EnsureInActorLocation(ApplicationUser user)
    {
        if (_tenant.LocationId is Guid scope && user.LocationId != scope)
            throw new UnauthorizedAccessException("You can only manage users at your own location.");
    }

    private float[] Embed(string? photoBase64) => EmbedBytes(DecodePhoto(photoBase64));

    private static byte[] DecodePhoto(string? photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            throw new InvalidOperationException("A face capture is required.");

        var raw = photoBase64.Contains(',') ? photoBase64.Split(',')[1] : photoBase64;
        try
        {
            return Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Photo data is invalid.");
        }
    }

    private float[] EmbedBytes(byte[] bytes)
    {
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
