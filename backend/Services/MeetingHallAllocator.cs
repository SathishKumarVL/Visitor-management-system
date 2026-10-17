using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>Hall picked for a check-in, or a warning when the location has halls but none can take the party.</summary>
public sealed record MeetingHallAllocation(MeetingHall? Hall, string? Warning);

public interface IMeetingHallAllocator
{
    /// <param name="skipHallIds">Halls lost to a concurrent check-in since they were last seen free.</param>
    Task<MeetingHallAllocation> AllocateAsync(VisitorVisit visit, IReadOnlyCollection<Guid>? skipHallIds = null);
}

/// <summary>
/// Smallest free hall at the visit's location that seats everyone. Equal-sized halls take turns: the one used
/// least recently wins. A hall is busy while a visit holding it is inside, so check-out frees it.
/// </summary>
public class MeetingHallAllocator : IMeetingHallAllocator
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public MeetingHallAllocator(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<MeetingHallAllocation> AllocateAsync(VisitorVisit visit, IReadOnlyCollection<Guid>? skipHallIds = null)
    {
        var locationIds = visit.VisitLocations.Select(l => l.LocationId).Distinct().ToList();
        if (locationIds.Count == 0 && _tenant.LocationId is Guid bound)
            locationIds.Add(bound);

        var halls = await _db.MeetingHalls.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.TenantId == visit.TenantId && h.IsActive)
            .Where(h => locationIds.Count == 0 || locationIds.Contains(h.LocationId))
            .ToListAsync();
        if (halls.Count == 0) return new MeetingHallAllocation(null, null);

        var persons = Math.Max(1, visit.NumberOfPersons);
        var fitting = halls.Where(h => h.SeatingCapacity >= persons).ToList();
        if (fitting.Count == 0)
            return new MeetingHallAllocation(null,
                $"No meeting hall seats {persons} {(persons == 1 ? "person" : "people")}. Checked in without a meeting hall.");

        // Visits are location-filtered for bound users; occupancy must count every visit in the tenant.
        var fittingIds = fitting.Select(h => h.Id).ToList();
        var busy = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == visit.TenantId && v.Id != visit.Id && v.Status == VisitStatus.Inside
                        && v.MeetingHallId != null && fittingIds.Contains(v.MeetingHallId.Value))
            .Select(v => v.MeetingHallId!.Value)
            .ToListAsync();
        var free = fitting
            .Where(h => !busy.Contains(h.Id) && (skipHallIds is null || !skipHallIds.Contains(h.Id)))
            .ToList();
        if (free.Count == 0)
            return new MeetingHallAllocation(null,
                $"All meeting halls that seat {persons} {(persons == 1 ? "person" : "people")} are in use. Checked in without a meeting hall.");

        var freeIds = free.Select(h => h.Id).ToList();
        var lastUsed = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == visit.TenantId && v.MeetingHallId != null && freeIds.Contains(v.MeetingHallId.Value))
            .GroupBy(v => v.MeetingHallId!.Value)
            .Select(g => new { HallId = g.Key, LastUsed = g.Max(v => v.CheckInAt) })
            .ToDictionaryAsync(x => x.HallId, x => x.LastUsed);

        var pick = free
            .OrderBy(h => h.SeatingCapacity)
            .ThenBy(h => lastUsed.GetValueOrDefault(h.Id))
            .ThenBy(h => h.Code, StringComparer.OrdinalIgnoreCase)
            .First();
        return new MeetingHallAllocation(pick, null);
    }
}
