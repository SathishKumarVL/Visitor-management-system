using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IEmergencyService
{
    Task<EmergencyRosterDto> GetRosterAsync(int warningMinutes);
    Task<EmergencyRollCallResultDto> RecordRollCallAsync(Guid visitId, EmergencyRollCallStatus status, string? notes, ClaimsPrincipal user);
}

/// <summary>
/// Evacuation roll call over the people currently on site. Marks are appended as events so the
/// visit record itself is never rewritten to carry an emergency state.
/// </summary>
public class EmergencyService : IEmergencyService
{
    private readonly ApplicationDbContext _db;
    private readonly IVisitorService _visitors;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenant;

    public EmergencyService(
        ApplicationDbContext db,
        IVisitorService visitors,
        IAuditService audit,
        ITenantContext tenant)
    {
        _db = db;
        _visitors = visitors;
        _audit = audit;
        _tenant = tenant;
    }

    private Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    public async Task<EmergencyRosterDto> GetRosterAsync(int warningMinutes)
    {
        var inside = await _visitors.GetInsideAsync(warningMinutes);
        var visitIds = inside.Select(v => v.VisitId).ToList();
        var latest = await LatestMarksAsync(visitIds);

        var items = inside.Select(v => ToRosterItem(v, latest.GetValueOrDefault(v.VisitId))).ToList();

        return new EmergencyRosterDto
        {
            TotalInside = items.Count,
            VisitorsInside = items.Count,
            // Employee presence is not tracked by this system, so it is reported as unavailable
            // rather than guessed at.
            EmployeeTrackingAvailable = false,
            Verified = items.Count(i => i.RollCallStatus == EmergencyRollCallStatus.Verified),
            Evacuated = items.Count(i => i.RollCallStatus == EmergencyRollCallStatus.Evacuated),
            Missing = items.Count(i => i.RollCallStatus == EmergencyRollCallStatus.Missing),
            Unaccounted = items.Count(i => i.RollCallStatus == EmergencyRollCallStatus.Unknown),
            Items = items
        };
    }

    public async Task<EmergencyRollCallResultDto> RecordRollCallAsync(
        Guid visitId,
        EmergencyRollCallStatus status,
        string? notes,
        ClaimsPrincipal user)
    {
        if (status == EmergencyRollCallStatus.Unknown)
            throw new InvalidOperationException("A roll-call outcome is required.");

        // The tenant filter makes this fail for a visit belonging to anyone else.
        var visit = await _db.VisitorVisits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        if (visit.Status != VisitStatus.Inside)
            throw new InvalidOperationException("Only a visitor currently inside can be marked in a roll call.");

        var mark = new EmergencyRollCallEvent
        {
            TenantId = CurrentTenantId,
            VisitorVisitId = visitId,
            Status = status,
            RecordedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        _db.EmergencyRollCallEvents.Add(mark);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("EmergencyRollCallMarked", "VisitorVisit", visitId.ToString(),
            $"Roll call marked {status}{(string.IsNullOrWhiteSpace(notes) ? "" : $": {notes.Trim()}")}");

        return new EmergencyRollCallResultDto
        {
            VisitId = visitId,
            RollCallStatus = mark.Status,
            RollCallAt = mark.RecordedAt
        };
    }

    /// <summary>Most recent mark per visit; earlier marks stay in the table as the audit trail.</summary>
    private async Task<Dictionary<Guid, EmergencyRollCallEvent>> LatestMarksAsync(IReadOnlyCollection<Guid> visitIds)
    {
        if (visitIds.Count == 0) return [];

        var events = await _db.EmergencyRollCallEvents.AsNoTracking()
            .Where(e => visitIds.Contains(e.VisitorVisitId))
            .ToListAsync();

        return events
            .GroupBy(e => e.VisitorVisitId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.RecordedAt).First());
    }

    private static EmergencyRosterItemDto ToRosterItem(VisitorListItemDto visit, EmergencyRollCallEvent? mark) => new()
    {
        VisitId = visit.VisitId,
        VisitNumber = visit.VisitNumber,
        VisitorName = visit.VisitorName,
        CompanyName = visit.CompanyName,
        HostName = visit.HostName,
        DepartmentName = visit.DepartmentName,
        Locations = visit.Locations,
        PhotoUrl = visit.PhotoUrl,
        CheckInAt = visit.CheckInAt,
        StatusLabel = visit.StatusLabel,
        NumberOfPersons = visit.NumberOfPersons,
        RollCallStatus = mark?.Status ?? EmergencyRollCallStatus.Unknown,
        RollCallAt = mark?.RecordedAt
    };
}
