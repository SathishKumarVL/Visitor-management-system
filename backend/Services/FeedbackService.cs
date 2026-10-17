using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IFeedbackService
{
    Task<IReadOnlyList<FeedbackQuestionDto>> GetQuestionsAsync(bool activeOnly = true, Guid? locationId = null);
    Task<FeedbackQuestionDto> UpsertQuestionAsync(Guid? id, FeedbackQuestionUpsertRequest request, string? user);
    Task DeactivateQuestionAsync(Guid id, string? user);
    Task<VisitFeedbackDto> SubmitForVisitAsync(Guid visitId, SubmitVisitFeedbackRequest request, string? user);
    Task<VisitFeedbackDto?> GetForVisitAsync(Guid visitId);
    Task<IReadOnlyList<VisitFeedbackDto>> ListResponsesAsync(int take = 50);
    Task<PagedResult<VisitFeedbackDto>> PageResponsesAsync(int page, int pageSize);
    Task EnsureFeedbackCompleteAsync(Guid visitId);
}

public sealed class FeedbackService : IFeedbackService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public FeedbackService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    private void EnsureInUserLocation(Guid? recordLocationId)
    {
        if (_tenant.LocationId is Guid scope && recordLocationId.HasValue && recordLocationId.Value != scope)
            throw new UnauthorizedAccessException("This feedback question belongs to another location.");
    }

    public async Task<IReadOnlyList<FeedbackQuestionDto>> GetQuestionsAsync(
        bool activeOnly = true,
        Guid? locationId = null)
    {
        if (_tenant.LocationId is Guid scope)
        {
            if (locationId.HasValue && locationId.Value != scope) return [];
            locationId = scope;
        }
        var q = _db.FeedbackQuestions.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        if (locationId.HasValue)
            q = q.Where(x => x.LocationId == null || x.LocationId == locationId.Value);

        return await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Prompt)
            .Select(x => new FeedbackQuestionDto
            {
                Id = x.Id,
                Prompt = x.Prompt,
                IsRequired = x.IsRequired,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder,
                LocationId = x.LocationId,
                LocationName = x.Location != null ? x.Location.Name : null
            })
            .ToListAsync();
    }

    public async Task<FeedbackQuestionDto> UpsertQuestionAsync(Guid? id, FeedbackQuestionUpsertRequest request, string? user)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new InvalidOperationException("Feedback question text is required.");
        request.LocationId ??= _tenant.LocationId;
        if (!request.LocationId.HasValue)
            throw new InvalidOperationException("Feedback question must be assigned to a location.");
        EnsureInUserLocation(request.LocationId);

        var locOk = await _db.Locations.AnyAsync(l => l.Id == request.LocationId.Value);
        if (!locOk) throw new InvalidOperationException("Selected location was not found.");

        FeedbackQuestion entity;
        if (id is Guid existingId)
        {
            entity = await _db.FeedbackQuestions.FirstOrDefaultAsync(x => x.Id == existingId)
                ?? throw new InvalidOperationException("Feedback question not found.");
            EnsureInUserLocation(entity.LocationId);
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = user;
        }
        else
        {
            entity = new FeedbackQuestion
            {
                TenantId = CurrentTenantId,
                CreatedBy = user
            };
            _db.FeedbackQuestions.Add(entity);
        }

        entity.Prompt = request.Prompt.Trim();
        entity.IsRequired = request.IsRequired;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.LocationId = request.LocationId;
        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(e => e.Location).LoadAsync();

        return new FeedbackQuestionDto
        {
            Id = entity.Id,
            Prompt = entity.Prompt,
            IsRequired = entity.IsRequired,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            LocationId = entity.LocationId,
            LocationName = entity.Location?.Name
        };
    }

    public async Task DeactivateQuestionAsync(Guid id, string? user)
    {
        var entity = await _db.FeedbackQuestions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Feedback question not found.");
        EnsureInUserLocation(entity.LocationId);
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user;
        await _db.SaveChangesAsync();
    }

    public async Task EnsureFeedbackCompleteAsync(Guid visitId)
    {
        var locationIds = await _db.VisitorVisitLocations.AsNoTracking()
            .Where(vl => vl.VisitorVisitId == visitId)
            .Select(vl => vl.LocationId)
            .ToListAsync();

        var hasQuestions = await ActiveQuestionsForLocationsQuery(locationIds).AnyAsync();
        if (!hasQuestions) return;

        var exists = await _db.VisitFeedbacks.AsNoTracking()
            .AnyAsync(f => f.VisitorVisitId == visitId);
        if (!exists)
            throw new InvalidOperationException("Please submit the visitor feedback form before check-out.");
    }

    public async Task<VisitFeedbackDto?> GetForVisitAsync(Guid visitId)
    {
        var row = await _db.VisitFeedbacks.AsNoTracking()
            .Include(f => f.Answers)
            .FirstOrDefaultAsync(f => f.VisitorVisitId == visitId);
        return row is null ? null : Map(row);
    }

    private IQueryable<VisitFeedback> ScopedResponses()
    {
        var q = _db.VisitFeedbacks.AsNoTracking();
        if (_tenant.LocationId is Guid scope)
            q = q.Where(f => _db.VisitorVisitLocations.Any(vl => vl.VisitorVisitId == f.VisitorVisitId && vl.LocationId == scope));
        return q;
    }

    public async Task<IReadOnlyList<VisitFeedbackDto>> ListResponsesAsync(int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await ScopedResponses()
            .Include(f => f.Answers)
            .OrderByDescending(f => f.SubmittedAt)
            .Take(take)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<PagedResult<VisitFeedbackDto>> PageResponsesAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = ScopedResponses();
        var total = await q.CountAsync();
        var rows = await q
            .Include(f => f.Answers)
            .OrderByDescending(f => f.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new PagedResult<VisitFeedbackDto>(rows.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<VisitFeedbackDto> SubmitForVisitAsync(Guid visitId, SubmitVisitFeedbackRequest request, string? user)
    {
        var visit = await _db.VisitorVisits
            .Include(v => v.Visitor)
            .Include(v => v.VisitLocations)
            .FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        if (visit.Status != VisitStatus.Inside)
            throw new InvalidOperationException("Feedback can only be collected for a visitor who is currently inside.");

        var existing = await _db.VisitFeedbacks
            .Include(f => f.Answers)
            .FirstOrDefaultAsync(f => f.VisitorVisitId == visitId);
        if (existing is not null)
            return Map(existing);

        var locationIds = visit.VisitLocations.Select(l => l.LocationId).ToList();
        var activeQuestions = await ActiveQuestionsForLocationsQuery(locationIds)
            .OrderBy(q => q.SortOrder)
            .ToListAsync();

        if (activeQuestions.Count == 0)
            throw new InvalidOperationException("No feedback questions are configured for this visit location.");

        var answersById = (request.Answers ?? [])
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last().Rating);

        foreach (var q in activeQuestions.Where(q => q.IsRequired))
        {
            if (!answersById.TryGetValue(q.Id, out var rating) || rating < 1 || rating > 5)
                throw new InvalidOperationException($"Please rate: {q.Prompt}");
        }

        foreach (var (questionId, rating) in answersById)
        {
            if (rating < 1 || rating > 5)
                throw new InvalidOperationException("Each rating must be between 1 and 5 stars.");
            if (activeQuestions.All(q => q.Id != questionId))
                throw new InvalidOperationException("One or more feedback answers refer to an unknown question.");
        }

        var feedback = new VisitFeedback
        {
            TenantId = visit.TenantId,
            VisitorVisitId = visit.Id,
            VisitorId = visit.VisitorId,
            VisitorName = visit.Visitor.FullName,
            CompanyName = visit.Visitor.CompanyName,
            Email = visit.Visitor.Email,
            Phone = visit.Visitor.Phone,
            VisitNumber = visit.VisitNumber,
            Comments = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim(),
            SubmittedBy = user,
            SubmittedAt = DateTime.UtcNow
        };

        foreach (var q in activeQuestions)
        {
            if (!answersById.TryGetValue(q.Id, out var rating)) continue;
            feedback.Answers.Add(new VisitFeedbackAnswer
            {
                FeedbackQuestionId = q.Id,
                QuestionText = q.Prompt,
                Rating = rating
            });
        }

        if (feedback.Answers.Count == 0)
            throw new InvalidOperationException("At least one star rating is required.");

        _db.VisitFeedbacks.Add(feedback);
        await _db.SaveChangesAsync();
        return Map(feedback);
    }

    /// <summary>Tenant-wide (null) questions plus any tied to the visit locations.</summary>
    private IQueryable<FeedbackQuestion> ActiveQuestionsForLocationsQuery(IReadOnlyList<Guid> locationIds)
    {
        var q = _db.FeedbackQuestions.AsNoTracking().Where(x => x.IsActive);
        if (locationIds.Count == 0)
            return q.Where(x => x.LocationId == null);
        return q.Where(x => x.LocationId == null || locationIds.Contains(x.LocationId.Value));
    }

    private static VisitFeedbackDto Map(VisitFeedback row) => new()
    {
        Id = row.Id,
        VisitId = row.VisitorVisitId,
        VisitorId = row.VisitorId,
        VisitorName = row.VisitorName,
        CompanyName = row.CompanyName,
        Email = row.Email,
        Phone = row.Phone,
        VisitNumber = row.VisitNumber,
        Comments = row.Comments,
        SubmittedAt = row.SubmittedAt,
        Answers = row.Answers
            .OrderBy(a => a.QuestionText)
            .Select(a => new VisitFeedbackAnswerDto
            {
                QuestionId = a.FeedbackQuestionId ?? Guid.Empty,
                QuestionText = a.QuestionText,
                Rating = a.Rating
            })
            .ToList()
    };
}
