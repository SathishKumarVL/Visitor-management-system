using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface ISiteService
{
    Task<IReadOnlyList<SiteDto>> GetAsync(bool activeOnly = false);
    Task<SiteDto> UpsertAsync(Guid? id, SiteUpsertRequest request, string? user);
    Task DeactivateAsync(Guid id, string? user);

    /// <summary>
    /// Site a newly created visit belongs to: the acting user's site when they are bound to one,
    /// otherwise the tenant default. Returns null only when the tenant has no active site at all.
    /// </summary>
    Task<Guid?> ResolveVisitSiteIdAsync();
}

public sealed class SiteService : ISiteService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEntitlementService _entitlements;

    public SiteService(
        ApplicationDbContext db,
        ITenantContext tenant,
        IEntitlementService entitlements)
    {
        _db = db;
        _tenant = tenant;
        _entitlements = entitlements;
    }

    private Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    public async Task<IReadOnlyList<SiteDto>> GetAsync(bool activeOnly = false)
    {
        var q = _db.Sites.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(s => s.IsActive);

        var sites = await q.OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync();
        if (sites.Count == 0) return Array.Empty<SiteDto>();

        var ids = sites.Select(s => s.Id).ToList();

        var userCounts = await _db.Users.AsNoTracking()
            .Where(u => u.SiteId != null && ids.Contains(u.SiteId.Value))
            .GroupBy(u => u.SiteId!.Value)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SiteId, x => x.Count);

        var locationCounts = await _db.Locations.AsNoTracking()
            .Where(l => l.SiteId != null && ids.Contains(l.SiteId.Value))
            .GroupBy(l => l.SiteId!.Value)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SiteId, x => x.Count);

        var insideCounts = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Inside && v.SiteId != null && ids.Contains(v.SiteId.Value))
            .GroupBy(v => v.SiteId!.Value)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SiteId, x => x.Count);

        return sites.Select(s => new SiteDto
        {
            Id = s.Id,
            Name = s.Name,
            Code = s.Code,
            Address = s.Address,
            IsActive = s.IsActive,
            IsDefault = s.IsDefault,
            UserCount = userCounts.TryGetValue(s.Id, out var uc) ? uc : 0,
            LocationCount = locationCounts.TryGetValue(s.Id, out var lc) ? lc : 0,
            InsideCount = insideCounts.TryGetValue(s.Id, out var ic) ? ic : 0
        }).ToList();
    }

    public async Task<SiteDto> UpsertAsync(Guid? id, SiteUpsertRequest request, string? user)
    {
        var tenantId = CurrentTenantId;
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Site name is required.");

        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();

        if (await _db.Sites.AnyAsync(s => s.Id != id && s.Name == name))
            throw new InvalidOperationException($"A site named '{name}' already exists.");

        if (code is not null && await _db.Sites.AnyAsync(s => s.Id != id && s.Code == code))
            throw new InvalidOperationException($"Site code '{code}' is already in use.");

        Site entity;
        if (id.HasValue)
        {
            entity = await _db.Sites.FirstOrDefaultAsync(s => s.Id == id.Value)
                ?? throw new InvalidOperationException("Site not found.");

            // Refuse to strand people and open visits inside a site that is being switched off.
            if (!request.IsActive && entity.IsActive)
                await EnsureSafeToCloseAsync(entity);

            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            await _entitlements.EnsureCanCreateSiteAsync();
            if (!request.IsActive)
                throw new InvalidOperationException("A new site must be created active.");

            entity = new Site { TenantId = tenantId, CreatedBy = user };
            _db.Sites.Add(entity);
        }

        entity.Name = name;
        entity.Code = code;
        entity.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        entity.IsActive = request.IsActive;

        if (request.IsDefault)
        {
            if (!request.IsActive)
                throw new InvalidOperationException("An inactive site cannot be the default.");

            foreach (var other in await _db.Sites.Where(s => s.IsDefault && s.Id != entity.Id).ToListAsync())
                other.IsDefault = false;

            entity.IsDefault = true;
        }
        else
        {
            // Every tenant must keep exactly one default, so clearing the flag needs a replacement.
            if (entity.IsDefault && !await _db.Sites.AnyAsync(s => s.IsDefault && s.Id != entity.Id))
                throw new InvalidOperationException(
                    "This is the default site. Make another site the default before clearing this one.");

            entity.IsDefault = false;
        }

        // The very first site of a tenant is the default whether or not the caller asked for it.
        if (!await _db.Sites.AnyAsync(s => s.IsDefault && s.Id != entity.Id) && entity.IsActive)
            entity.IsDefault = true;

        await _db.SaveChangesAsync();

        return (await GetAsync()).First(s => s.Id == entity.Id);
    }

    public async Task DeactivateAsync(Guid id, string? user)
    {
        var site = await _db.Sites.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Site not found.");

        if (!site.IsActive) return;

        await EnsureSafeToCloseAsync(site);

        site.IsActive = false;
        site.IsDefault = false;
        site.UpdatedAt = DateTime.UtcNow;
        site.UpdatedBy = user;
        await _db.SaveChangesAsync();
    }

    public async Task<Guid?> ResolveVisitSiteIdAsync()
    {
        if (_tenant.SiteId is Guid bound && bound != Guid.Empty)
            return bound;

        var tenantId = CurrentTenantId;
        return await _db.Sites.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// A site can only be closed once nobody is inside it and no user depends on it, otherwise those
    /// users would log in with a site claim that resolves to nothing and silently lose all visibility.
    /// </summary>
    private async Task EnsureSafeToCloseAsync(Site site)
    {
        var inside = await _db.VisitorVisits
            .CountAsync(v => v.SiteId == site.Id && v.Status == VisitStatus.Inside);
        if (inside > 0)
            throw new InvalidOperationException(
                $"{inside} visitor(s) are still inside {site.Name}. Check them out before deactivating the site.");

        var users = await _db.Users.CountAsync(u => u.SiteId == site.Id && u.IsActive);
        if (users > 0)
            throw new InvalidOperationException(
                $"{users} active user(s) are assigned to {site.Name}. Reassign them before deactivating the site.");

        if (!await _db.Sites.AnyAsync(s => s.IsActive && s.Id != site.Id))
            throw new InvalidOperationException("At least one active site is required.");
    }
}
