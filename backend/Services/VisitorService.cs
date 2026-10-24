using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services.Face;

namespace Tiaano.Vms.Api.Services;

public interface IVisitorService
{
    Task<VisitorDetailDto> RegisterAsync(RegisterVisitorRequest request, ClaimsPrincipal user, string webRoot);
    Task<VisitorDetailDto> CreateExpectedAsync(ExpectedVisitorRequest request, ClaimsPrincipal user);
    Task<VisitorDetailDto> UpdateExpectedAsync(Guid visitId, ExpectedVisitorRequest request, ClaimsPrincipal user);
    Task<PagedResult<VisitorListItemDto>> SearchAsync(VisitorSearchRequest request, ClaimsPrincipal user, int warningMinutes);
    Task<VisitorDetailDto?> GetAsync(Guid visitId, ClaimsPrincipal user, string encryptionKey);
    Task<VisitorListItemDto> ApproveAsync(Guid visitId, ClaimsPrincipal user);
    Task<VisitorListItemDto> RejectAsync(Guid visitId, string reason, ClaimsPrincipal user);
    Task<PassDto> CheckInAsync(Guid visitId, CheckInRequest request, ClaimsPrincipal user, string webRoot);
    Task<VisitorListItemDto> CheckOutAsync(Guid visitId, CheckOutRequest request, ClaimsPrincipal user);
    /// <summary>System job: check out Inside visits past the max stay; counts as manual check-out.</summary>
    Task<int> AutoCheckOutStaleVisitsAsync(TimeSpan maxStay, CancellationToken cancellationToken = default);
    Task<VisitorDetailDto> SaveMeetingMinutesAsync(Guid visitId, SaveMeetingMinutesRequest request, ClaimsPrincipal user);
    /// <summary>Waives minutes of meeting so the check-out email is sent without them.</summary>
    Task<VisitorDetailDto> SetMomNotRequiredAsync(Guid visitId, SetMomNotRequiredRequest request, ClaimsPrincipal user);
    /// <summary>Pass <paramref name="user"/> to limit Host logins to their own visitors; null returns everyone (system use).</summary>
    Task<IReadOnlyList<VisitorListItemDto>> GetInsideAsync(int warningMinutes, ClaimsPrincipal? user = null);
    Task<IReadOnlyList<VisitorListItemDto>> GetExpectedAsync(DateOnly? date = null, ClaimsPrincipal? user = null);
    Task<IReadOnlyList<BookingMatchDto>> FindTodaysBookingsAsync(string? phone, Guid? visitorId);
    Task<VisitorPhoneLookupDto?> LookupByPhoneAsync(string? phone);
    Task<IReadOnlyList<PreviousPurposeAnswersDto>> GetPreviousPurposeAnswersAsync(Guid visitorId);
    Task<LookupRecordVisitorsDto> GetLookupRecordVisitorsAsync(Guid sourceId, string recordId, Guid? visitorId);
    Task<IReadOnlyList<VisitorListItemDto>> GetPendingApprovalsAsync(ClaimsPrincipal user);
    Task<PassDto?> GetPassAsync(Guid visitId, string webRoot);
    Task<PassDto> ReprintPassAsync(Guid visitId, ClaimsPrincipal user, string webRoot);
    Task<PassDto?> LookupByVisitNumberAsync(string visitNumber, string webRoot);
    Task<DashboardDto> GetDashboardAsync(ClaimsPrincipal user, int warningMinutes);
    Task<IReadOnlyList<MomNotSentByEmployeeDto>> GetMomNotSentByEmployeeAsync(ClaimsPrincipal user);
    Task<FaceSearchMatchDto?> FaceSearchAsync(string photoBase64, ClaimsPrincipal user);
    Task<FaceCheckoutMatchDto?> FaceIdentifyInsideAsync(string photoBase64, ClaimsPrincipal user);
    /// <summary>Validates that a visitor photo contains exactly one detectable face (no persistence).</summary>
    FacePhotoValidationDto ValidateVisitorPhoto(string photoBase64);
}

public class VisitorService : IVisitorService
{
    private readonly ApplicationDbContext _db;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private const int ExpectedBookingGraceMinutes = 5;
    public const string LocationMomNotRequiredBy = "Location setting";

    private readonly IHostEnvironment _env;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VisitorService> _logger;
    private readonly IMediaStorageService _media;
    private readonly ITenantContext _tenant;
    private readonly IFaceEmbeddingService _faces;
    private readonly IPassNumberService _passNumbers;
    private readonly IFeedbackService _feedback;
    private readonly IVisitMailService _visitMail;
    private readonly IMeetingHallAllocator _meetingHalls;
    private static readonly Regex TenDigitPhone = new(@"^\d{10}$", RegexOptions.Compiled);

    public VisitorService(
        ApplicationDbContext db,
        ISettingsService settings,
        INotificationService notifications,
        IConfiguration config,
        IHostEnvironment env,
        IServiceScopeFactory scopeFactory,
        ILogger<VisitorService> logger,
        IMediaStorageService media,
        ITenantContext tenant,
        IFaceEmbeddingService faces,
        IPassNumberService passNumbers,
        IFeedbackService feedback,
        IVisitMailService visitMail,
        IMeetingHallAllocator meetingHalls)
    {
        _db = db;
        _settings = settings;
        _notifications = notifications;
        _config = config;
        _env = env;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _media = media;
        _tenant = tenant;
        _faces = faces;
        _passNumbers = passNumbers;
        _feedback = feedback;
        _visitMail = visitMail;
        _meetingHalls = meetingHalls;
    }

    private string EncryptionKey() => SecretConfiguration.GetRequiredEncryptionKey(_config, _env);

    public async Task<VisitorDetailDto> RegisterAsync(RegisterVisitorRequest request, ClaimsPrincipal user, string webRoot)
    {
        ValidateRegistration(request);
        var clientRequestId = request.ClientRequestId is Guid sent && sent != Guid.Empty ? sent : (Guid?)null;
        if (clientRequestId is Guid resubmitted && await VisitForClientRequestAsync(resubmitted) is Guid alreadyRegistered)
            return (await BuildDetailAsync(alreadyRegistered, user, EncryptionKey()))!;

        await VisitPurposeDefaults.EnsureOthersPurposeAsync(_db, TenantClaims.RequireTenantId(user, _tenant));
        if (!string.IsNullOrWhiteSpace(request.PurposeNotes))
        {
            request.PurposeNotes = request.PurposeNotes.Trim();
            var othersId = (await _db.VisitPurposes.AsNoTracking()
                .FirstAsync(p => p.Name == VisitPurposeDefaults.OthersName)).Id;
            if (!request.PurposeIds.Contains(othersId))
                request.PurposeIds.Add(othersId);
        }

        if (request.PurposeIds.Count == 0)
            throw new InvalidOperationException("Purpose of visit is required.");

        var settings = await _settings.GetAsync();

        if (settings.PhotoRequired && string.IsNullOrWhiteSpace(request.PhotoBase64))
            throw new InvalidOperationException("Visitor photo is required.");

        var hasIdType = !string.IsNullOrWhiteSpace(request.IdTypeName)
            || (request.IdTypeId is Guid tid && tid != Guid.Empty);
        var hasIdNumber = !string.IsNullOrWhiteSpace(request.IdNumber);
        if (settings.IdVerificationRequired && (!hasIdType || !hasIdNumber))
            throw new InvalidOperationException("ID type and ID number are required.");
        if (hasIdType != hasIdNumber)
            throw new InvalidOperationException("Provide both ID type and ID number, or leave both blank.");
        if (hasIdNumber && request.IdNumber!.Trim().Length > 40)
            throw new InvalidOperationException("ID number must be at most 40 characters.");
        Guid? idTypeId = hasIdType ? await ResolveOptionalIdTypeAsync(request) : null;

        var host = await ResolveHostAsync(request.DepartmentId, request.HostEmployeeId, request.HostName, user.Identity?.Name);

        var groupEmployeeUserId = string.IsNullOrWhiteSpace(request.GroupEmployeeUserId) ? null : request.GroupEmployeeUserId.Trim();
        var registeringTenantId = ResolveTenantId(user);
        if (groupEmployeeUserId is not null
            && !await _db.Users.AnyAsync(u => u.Id == groupEmployeeUserId && u.TenantId == registeringTenantId && u.IsActive))
            throw new InvalidOperationException("The recognised employee login was not found or is inactive.");
        var groupEmployeeId = request.GroupEmployeeId is Guid requestedEmployeeId && requestedEmployeeId != Guid.Empty
            ? requestedEmployeeId
            : (Guid?)null;
        if (groupEmployeeId is Guid geId)
        {
            var groupEmployee = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == geId && e.TenantId == registeringTenantId && e.IsActive)
                .Select(e => new { e.UserId })
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("The recognised employee was not found or is inactive.");
            groupEmployeeUserId ??= groupEmployee.UserId;
        }

        var purposes = await _db.VisitPurposes.Where(p => request.PurposeIds.Contains(p.Id) && p.IsActive).ToListAsync();
        if (purposes.Count == 0) throw new InvalidOperationException("At least one valid purpose is required.");
        var lookupSecret = EncryptionKey();
        var purposeFieldValues = PurposeFields.BuildValues(
            purposes,
            request.PurposeFields,
            token => LookupTokens.Unprotect(token, lookupSecret, DateTimeOffset.UtcNow) is { } s && s.TenantId == _tenant.TenantId ? s : null,
            request.Telephone,
            request.Email);

        var locationIds = request.LocationIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (_tenant.LocationId is Guid boundLocation && boundLocation != Guid.Empty
            && !locationIds.Contains(boundLocation))
            locationIds.Add(boundLocation);
        await AddHostLocationIfNoneAsync(locationIds, host);
        var locations = locationIds.Count == 0
            ? []
            : await _db.Locations.Where(l => locationIds.Contains(l.Id) && l.IsActive).ToListAsync();
        if (locationIds.Count > 0 && locations.Count == 0)
            throw new InvalidOperationException("At least one valid location is required.");
        if (_tenant.LocationId is Guid requiredLoc && !locations.Any(l => l.Id == requiredLoc))
            throw new InvalidOperationException("Visits must include your assigned location.");
        // Always allocate from the location (or tenant) series — free-text desk badges collide with unique PassCode.
        Guid? passLocationId = locations.Select(l => (Guid?)l.Id).FirstOrDefault()
            ?? (_tenant.LocationId is Guid tl && tl != Guid.Empty ? tl : null);
        var passNumber = await _passNumbers.AllocateNextAsync(passLocationId);

        // A booked visitor registered through the walk-in form still closes their booking, as long as
        // it is the same person (mobile or face match) meeting the same host today.
        if (!request.ExpectedVisitId.HasValue && !request.SkipBookingMatch)
        {
            var bookingId = await TodaysBookingsFor(TryNormalizePhone(request.Telephone), request.RecognizedVisitorId)
                .Where(v => v.HostEmployeeId == host.Id)
                .OrderBy(v => v.ExpectedTime)
                .Select(v => (Guid?)v.Id)
                .FirstOrDefaultAsync();
            if (bookingId.HasValue)
            {
                request.ExpectedVisitId = bookingId;
                request.IsWalkIn = false;
            }
        }

        Visitor visitor;
        Visitor? newVisitor = null;
        VisitorVisit? booking = null;
        if (request.ExpectedVisitId.HasValue)
        {
            var expected = await _db.VisitorVisits
                .Include(v => v.Visitor)
                .Include(v => v.VisitPurposes)
                .Include(v => v.VisitLocations)
                .FirstOrDefaultAsync(v => v.Id == request.ExpectedVisitId.Value)
                ?? throw new InvalidOperationException("Expected visitor record not found.");
            if (expected.Status != VisitStatus.Expected)
                throw new InvalidOperationException("This booking has already been used or cancelled.");
            // A confirmed face match keeps the visit in the returning visitor's history, even when the
            // booking was made under a fresh visitor record.
            var recognized = request.RecognizedVisitorId is Guid rid && rid != expected.VisitorId
                ? await _db.Visitors.FirstOrDefaultAsync(v => v.Id == rid)
                : null;
            if (recognized is not null)
                await EnsureVisitorNotAlreadyInsideAsync(recognized.Id);
            visitor = recognized ?? expected.Visitor;
            ApplyReturningVisitorProfile(visitor, request, user.Identity?.Name);
            // The booking becomes the visit, so one appointment is one entry with one visit number.
            booking = expected;
        }
        else if (request.RecognizedVisitorId.HasValue)
        {
            // Face match the operator confirmed: reuse the visitor so the return visit joins their
            // history. The query filter keeps this from reaching across tenants or locations.
            visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == request.RecognizedVisitorId.Value)
                ?? throw new InvalidOperationException("Recognised visitor record not found.");
            await EnsureVisitorNotAlreadyInsideAsync(visitor.Id);
            // Fill gaps from earlier registrations (e.g. Gender) without wiping existing contact fields.
            ApplyReturningVisitorProfile(visitor, request, user.Identity?.Name);
        }
        else
        {
            // Even without an explicit match confirmation, block if the capture matches someone already inside.
            if (!string.IsNullOrWhiteSpace(request.PhotoBase64) && _faces.IsAvailable)
            {
                var matchedId = await TryFindMatchingVisitorIdAsync(request.PhotoBase64);
                if (matchedId is Guid mid)
                    await EnsureVisitorNotAlreadyInsideAsync(mid);
            }

            visitor = new Visitor
            {
                TenantId = ResolveTenantId(user),
                VisitorNumber = await NextVisitorNumberAsync(settings.VisitorIdPrefix),
                FullName = request.VisitorName.Trim(),
                Gender = request.Gender,
                CompanyName = request.CompanyName.Trim(),
                Phone = request.Telephone?.Trim(),
                Email = request.Email?.Trim(),
                CreatedBy = user.Identity?.Name
            };
            _db.Visitors.Add(visitor);
            newVisitor = visitor;
        }

        await MarkNewPeopleForRecordsAsync(purposeFieldValues, purposes, visitor.Id);

        // Approval is tenant configuration: walk-ins and scheduled visits can be gated independently.
        var needsApproval = request.IsWalkIn ? settings.WalkInApprovalRequired : settings.ApprovalRequired;
        var visit = booking ?? new VisitorVisit
        {
            TenantId = ResolveTenantId(user),
            VisitNumber = await NextVisitNumberAsync(settings.VisitorIdPrefix),
            CreatedBy = user.Identity?.Name
        };
        visit.Visitor = visitor;
        visit.VisitorType = request.IsWalkIn ? VisitorType.WalkIn : VisitorType.Expected;
        visit.Status = needsApproval ? VisitStatus.PendingApproval : VisitStatus.Approved;
        visit.VisitDate = request.VisitDate ?? AppTime.Today;
        visit.VisitTime = request.VisitTime ?? TimeOnly.FromDateTime(AppTime.Now);
        visit.DepartmentId = request.DepartmentId;
        visit.HostEmployeeId = host.Id;
        visit.HostIntercom = null;
        visit.PlantNumber = request.PlantNumber;
        visit.OtherLocationText = request.OtherLocationText;
        visit.PurposeNotes = request.PurposeNotes;
        visit.PurposeFieldsJson = PurposeFields.Write(purposeFieldValues);
        visit.Notes = booking is not null && string.IsNullOrWhiteSpace(request.Notes) ? booking.Notes : request.Notes;
        visit.PassNumber = passNumber;
        visit.NumberOfPersons = request.NumberOfPersons < 1 ? 1 : Math.Min(request.NumberOfPersons, 99);
        visit.IsWalkIn = request.IsWalkIn;
        visit.IsGroupEmployee = groupEmployeeUserId is not null || groupEmployeeId is not null;
        visit.GroupEmployeeUserId = groupEmployeeUserId;
        visit.GroupEmployeeId = groupEmployeeId;
        visit.ClientRequestId = clientRequestId;
        if (booking is not null)
        {
            visit.UpdatedAt = DateTime.UtcNow;
            visit.UpdatedBy = user.Identity?.Name;
        }
        if (locations.Count > 0 && locations.All(l => !l.MomRequired))
        {
            visit.MomNotRequired = true;
            visit.MomNotRequiredBy = LocationMomNotRequiredBy;
            visit.MomNotRequiredAt = DateTime.UtcNow;
        }

        var purposeIdSet = purposes.Select(p => p.Id).ToHashSet();
        foreach (var stale in visit.VisitPurposes.Where(p => !purposeIdSet.Contains(p.VisitPurposeId)).ToList())
            visit.VisitPurposes.Remove(stale);
        foreach (var p in purposes.Where(p => visit.VisitPurposes.All(x => x.VisitPurposeId != p.Id)).ToList())
        {
            var link = new VisitorVisitPurpose { VisitPurposeId = p.Id };
            visit.VisitPurposes.Add(link);
            if (booking is not null) _db.VisitorVisitPurposes.Add(link);
        }
        var locationIdSet = locations.Select(l => l.Id).ToHashSet();
        foreach (var stale in visit.VisitLocations.Where(l => !locationIdSet.Contains(l.LocationId)).ToList())
            visit.VisitLocations.Remove(stale);
        foreach (var l in locations.Where(l => visit.VisitLocations.All(x => x.LocationId != l.Id)).ToList())
        {
            var link = new VisitorVisitLocation { LocationId = l.Id };
            visit.VisitLocations.Add(link);
            if (booking is not null) _db.VisitorVisitLocations.Add(link);
        }

        var approval = needsApproval
            ? new Approval
            {
                Status = ApprovalStatus.Pending,
                HostEmployeeId = host.Id,
                RequestedToUserId = host.UserId
            }
            : new Approval
            {
                Status = ApprovalStatus.Approved,
                HostEmployeeId = host.Id,
                ActionByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ActionAt = DateTime.UtcNow,
                Notes = "Auto-approved based on settings"
            };
        visit.Approvals.Add(approval);
        // Its Id is set client-side, so on an already-saved booking EF would otherwise issue an UPDATE.
        if (booking is not null) _db.Approvals.Add(approval);

        if (booking is null) _db.VisitorVisits.Add(visit);
        try
        {
            await SaveNewVisitAsync(visit, newVisitor, settings.VisitorIdPrefix, keepVisitNumber: booking is not null);
        }
        catch (DbUpdateException ex) when (clientRequestId is Guid racing && IsClientRequestTaken(ex))
        {
            // The same form arrived twice at once; the other request registered it.
            _db.ChangeTracker.Clear();
            await _passNumbers.ReleaseHoldAsync(passNumber);
            var winner = await VisitForClientRequestAsync(racing)
                ?? throw new InvalidOperationException("This registration is already being saved. Refresh the visitor list.");
            return (await BuildDetailAsync(winner, user, EncryptionKey()))!;
        }

        if (!string.IsNullOrWhiteSpace(request.PhotoBase64))
        {
            await SavePhotoAsync(visitor.Id, visit.Id, request.PhotoBase64, webRoot, user.Identity?.Name);
            await EnrolFaceAsync(visitor.Id, request.PhotoBase64, user);
        }

        if (idTypeId is Guid resolvedIdType && hasIdNumber)
        {
            var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
            _db.VisitorDocuments.Add(new VisitorDocument
            {
                VisitorId = visitor.Id,
                VisitorVisitId = visit.Id,
                IdTypeId = resolvedIdType,
                IdNumberEncrypted = SensitiveDataHelper.Encrypt(request.IdNumber!.Trim(), key),
                IdNumberMasked = SensitiveDataHelper.MaskId(request.IdNumber.Trim()),
                VerificationStatus = IdVerificationStatus.Verified,
                CreatedBy = user.Identity?.Name
            });
        }
        await _db.SaveChangesAsync();

        // A pass is only valid for a visit that is cleared to enter; pending-approval visits get one at check-in.
        if (visit.Status == VisitStatus.Approved)
        {
            _db.VisitorPasses.Add(new VisitorPass
            {
                VisitorVisitId = visit.Id,
                PassCode = passNumber,
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = DateTime.UtcNow.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            });
            await _db.SaveChangesAsync();
        }

        if (needsApproval)
        {
            var body = $"Visitor {visitor.FullName} from {visitor.CompanyName} is pending your approval.";
            if (!string.IsNullOrWhiteSpace(host.UserId))
                QueueHostPush(visit.TenantId, host.UserId, "Visitor approval required", body,
                    $"approval-push:{visit.Id}", $"/visitors/{visit.Id}");
            else
                await _notifications.NotifyAsync("Internal", host.Email ?? "host", "Visitor approval required", body);
        }

        try
        {
            return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not OperationCanceledException)
        {
            // The visit is saved; reporting an error here would make the desk register the visitor again.
            _logger.LogWarning(ex, "Visit {VisitNumber} was registered but reading it back failed; returning the saved summary.", visit.VisitNumber);
            return RegisteredSummary(visit, visitor, host);
        }
    }

    private Task<Guid?> VisitForClientRequestAsync(Guid clientRequestId) =>
        _db.VisitorVisits.AsNoTracking()
            .Where(v => v.ClientRequestId == clientRequestId)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync();

    private static bool IsClientRequestTaken(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(ApplicationDbContext.ClientRequestIndex, StringComparison.OrdinalIgnoreCase);

    private static VisitorDetailDto RegisteredSummary(VisitorVisit visit, Visitor visitor, Employee host) => new()
    {
        VisitId = visit.Id,
        VisitorId = visitor.Id,
        VisitorNumber = visitor.VisitorNumber,
        VisitNumber = visit.VisitNumber,
        VisitorName = visitor.FullName,
        CompanyName = visitor.CompanyName,
        Gender = visitor.Gender,
        Salutation = SalutationFor(visitor.Gender),
        Phone = visitor.Phone,
        Email = visitor.Email,
        HostName = host.FullName,
        DepartmentName = host.Department?.Name ?? string.Empty,
        VisitDate = visit.VisitDate,
        VisitTime = visit.VisitTime,
        Status = visit.Status,
        StatusLabel = visit.Status.ToString(),
        PassCode = visit.Status == VisitStatus.Approved ? visit.PassNumber : null,
        NumberOfPersons = visit.NumberOfPersons,
        IsGroupEmployee = visit.IsGroupEmployee,
        PurposeNotes = visit.PurposeNotes,
        Notes = visit.Notes
    };

    public async Task<VisitorDetailDto> CreateExpectedAsync(ExpectedVisitorRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorName) || string.IsNullOrWhiteSpace(request.CompanyName))
            throw new InvalidOperationException("Visitor name and company are required.");
        if (request.Gender is not (VisitorGender.Male or VisitorGender.Female))
            throw new InvalidOperationException("Gender is required (Male or Female).");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new InvalidOperationException("Visitor mobile number is required.");
        // The grace absorbs clock drift between the booking device and the server.
        if (request.ExpectedDate.ToDateTime(request.ExpectedTime) < AppTime.Now.AddMinutes(-ExpectedBookingGraceMinutes))
            throw new InvalidOperationException("Appointment date and time cannot be in the past.");

        var purposeIds = await ResolveBookingPurposeIdsAsync(request, user);

        // A location-bound user only sees visits that include their location, so their bookings must carry it.
        var locationIds = request.LocationIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (_tenant.LocationId is Guid boundLocation && boundLocation != Guid.Empty
            && !locationIds.Contains(boundLocation))
            locationIds.Add(boundLocation);

        var settings = await _settings.GetAsync();
        var host = await ResolveHostAsync(request.DepartmentId, request.HostEmployeeId, request.HostName, user.Identity?.Name);
        await AddHostLocationIfNoneAsync(locationIds, host);
        var phone = NormalizeTenDigitPhone(request.Phone!);

        var duplicate = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Expected
                        && v.Visitor.Phone == phone
                        && v.HostEmployeeId == host.Id
                        && (v.ExpectedDate ?? v.VisitDate) == request.ExpectedDate)
            .Select(v => new { v.VisitNumber, Time = v.ExpectedTime ?? v.VisitTime })
            .FirstOrDefaultAsync();
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"This visitor already has an appointment with {host.FullName} on {request.ExpectedDate:dd/MM/yyyy} " +
                $"at {duplicate.Time:HH:mm} (booking {duplicate.VisitNumber}).");

        // One person, one visitor record: a known mobile number books against the existing visitor, so their
        // history, face match and booking all point at the same record.
        var visitor = await VisitorsByPhone(phone).FirstOrDefaultAsync();
        Visitor? newVisitor = null;
        if (visitor is null)
        {
            visitor = new Visitor
            {
                TenantId = ResolveTenantId(user),
                VisitorNumber = await NextVisitorNumberAsync(settings.VisitorIdPrefix),
                Phone = phone,
                CreatedBy = user.Identity?.Name
            };
            _db.Visitors.Add(visitor);
            newVisitor = visitor;
        }
        else
        {
            visitor.UpdatedAt = DateTime.UtcNow;
            visitor.UpdatedBy = user.Identity?.Name;
        }
        visitor.FullName = request.VisitorName.Trim();
        visitor.Gender = request.Gender;
        visitor.CompanyName = request.CompanyName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Email))
            visitor.Email = request.Email.Trim();

        var visit = new VisitorVisit
        {
            TenantId = ResolveTenantId(user),
            Visitor = visitor,
            VisitNumber = await NextVisitNumberAsync(settings.VisitorIdPrefix),
            PreRegistrationReference = $"EXP-{AppTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            VisitorType = VisitorType.PreRegistered,
            Status = VisitStatus.Expected,
            VisitDate = request.ExpectedDate,
            VisitTime = request.ExpectedTime,
            ExpectedDate = request.ExpectedDate,
            ExpectedTime = request.ExpectedTime,
            DepartmentId = request.DepartmentId,
            HostEmployeeId = host.Id,
            PlantNumber = request.PlantNumber,
            OtherLocationText = request.OtherLocationText,
            PurposeNotes = request.PurposeNotes,
            Notes = request.Notes,
            CreatedBy = user.Identity?.Name
        };

        foreach (var pid in purposeIds)
            visit.VisitPurposes.Add(new VisitorVisitPurpose { VisitPurposeId = pid });
        foreach (var lid in locationIds)
            visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = lid });

        _db.VisitorVisits.Add(visit);
        await SaveNewVisitAsync(visit, newVisitor, settings.VisitorIdPrefix);

        // Reminders are best-effort: a notify failure must not roll back the appointment.
        try
        {
            await TryQueueAppointmentAckAsync(visit, visitor, host);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Appointment acknowledgement notify failed for visit {VisitId}", visit.Id);
        }

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;
    }

    /// <summary>Edits a booking that has not arrived yet. A changed date, time or host re-sends the appointment mail.</summary>
    public async Task<VisitorDetailDto> UpdateExpectedAsync(Guid visitId, ExpectedVisitorRequest request, ClaimsPrincipal user)
    {
        var visit = await _db.VisitorVisits
            .Include(v => v.Visitor)
            .Include(v => v.VisitPurposes)
            .Include(v => v.VisitLocations)
            .FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Booking not found.");
        if (visit.Status != VisitStatus.Expected)
            throw new InvalidOperationException("Only bookings for visitors who have not arrived yet can be edited.");

        if (string.IsNullOrWhiteSpace(request.VisitorName) || string.IsNullOrWhiteSpace(request.CompanyName))
            throw new InvalidOperationException("Visitor name and company are required.");
        if (request.Gender is not (VisitorGender.Male or VisitorGender.Female))
            throw new InvalidOperationException("Gender is required (Male or Female).");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new InvalidOperationException("Visitor mobile number is required.");

        var oldDate = visit.ExpectedDate ?? visit.VisitDate;
        var oldTime = visit.ExpectedTime ?? visit.VisitTime;
        var whenChanged = oldDate != request.ExpectedDate || oldTime != request.ExpectedTime;
        if (whenChanged && request.ExpectedDate.ToDateTime(request.ExpectedTime) < AppTime.Now.AddMinutes(-ExpectedBookingGraceMinutes))
            throw new InvalidOperationException("Appointment date and time cannot be in the past.");

        var purposeIds = await ResolveBookingPurposeIdsAsync(request, user);
        var host = await ResolveHostAsync(request.DepartmentId, request.HostEmployeeId, request.HostName, user.Identity?.Name);
        var phone = NormalizeTenDigitPhone(request.Phone!);

        var duplicate = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Id != visit.Id
                        && v.Status == VisitStatus.Expected
                        && v.Visitor.Phone == phone
                        && v.HostEmployeeId == host.Id
                        && (v.ExpectedDate ?? v.VisitDate) == request.ExpectedDate)
            .Select(v => new { v.VisitNumber, Time = v.ExpectedTime ?? v.VisitTime })
            .FirstOrDefaultAsync();
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"This visitor already has an appointment with {host.FullName} on {request.ExpectedDate:dd/MM/yyyy} " +
                $"at {duplicate.Time:HH:mm} (booking {duplicate.VisitNumber}).");

        // Same rule as booking: a mobile number that already belongs to a visitor moves the booking to that visitor.
        var visitor = visit.Visitor;
        if (visitor.Phone != phone)
        {
            var existing = await VisitorsByPhone(phone).FirstOrDefaultAsync(v => v.Id != visitor.Id);
            if (existing is not null)
            {
                visit.Visitor = existing;
                visitor = existing;
            }
            else
            {
                visitor.Phone = phone;
            }
        }
        visitor.FullName = request.VisitorName.Trim();
        visitor.Gender = request.Gender;
        visitor.CompanyName = request.CompanyName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Email))
            visitor.Email = request.Email.Trim();
        visitor.UpdatedAt = DateTime.UtcNow;
        visitor.UpdatedBy = user.Identity?.Name;

        var hostChanged = visit.HostEmployeeId != host.Id;
        visit.VisitDate = request.ExpectedDate;
        visit.VisitTime = request.ExpectedTime;
        visit.ExpectedDate = request.ExpectedDate;
        visit.ExpectedTime = request.ExpectedTime;
        visit.DepartmentId = request.DepartmentId;
        visit.HostEmployeeId = host.Id;
        visit.PurposeNotes = request.PurposeNotes;
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;

        foreach (var stale in visit.VisitPurposes.Where(p => !purposeIds.Contains(p.VisitPurposeId)).ToList())
            visit.VisitPurposes.Remove(stale);
        foreach (var pid in purposeIds.Where(id => visit.VisitPurposes.All(p => p.VisitPurposeId != id)))
            visit.VisitPurposes.Add(new VisitorVisitPurpose { VisitPurposeId = pid });

        var locationIds = request.LocationIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (_tenant.LocationId is Guid boundLocation && boundLocation != Guid.Empty && !locationIds.Contains(boundLocation))
            locationIds.Add(boundLocation);
        if (visit.VisitLocations.Count == 0)
            await AddHostLocationIfNoneAsync(locationIds, host);
        if (locationIds.Count > 0)
        {
            foreach (var stale in visit.VisitLocations.Where(l => !locationIds.Contains(l.LocationId)).ToList())
                visit.VisitLocations.Remove(stale);
            foreach (var lid in locationIds.Where(id => visit.VisitLocations.All(l => l.LocationId != id)))
                visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = lid });
        }

        await _db.SaveChangesAsync();

        if (whenChanged || hostChanged)
        {
            try
            {
                var reloaded = await _db.VisitorVisits
                    .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
                    .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
                    .Include(v => v.Department)
                    .FirstAsync(v => v.Id == visit.Id);
                await TryQueueAppointmentAckAsync(reloaded, visitor, host, $":updated-{DateTime.UtcNow.Ticks}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Updated appointment notify failed for visit {VisitId}", visit.Id);
            }
        }

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;
    }

    /// <summary>Booking purposes: typed "Others" text implies the Others purpose; at least one active purpose.</summary>
    private async Task<List<Guid>> ResolveBookingPurposeIdsAsync(ExpectedVisitorRequest request, ClaimsPrincipal user)
    {
        await VisitPurposeDefaults.EnsureOthersPurposeAsync(_db, TenantClaims.RequireTenantId(user, _tenant));
        request.PurposeNotes = string.IsNullOrWhiteSpace(request.PurposeNotes) ? null : request.PurposeNotes.Trim();
        if (request.PurposeNotes is not null)
        {
            var othersId = (await _db.VisitPurposes.AsNoTracking()
                .FirstAsync(p => p.Name == VisitPurposeDefaults.OthersName)).Id;
            if (!request.PurposeIds.Contains(othersId))
                request.PurposeIds.Add(othersId);
        }
        if (request.PurposeIds.Count == 0)
            throw new InvalidOperationException("Purpose of visit is required.");
        var purposeIds = await _db.VisitPurposes
            .Where(p => request.PurposeIds.Contains(p.Id) && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();
        if (purposeIds.Count == 0)
            throw new InvalidOperationException("At least one valid purpose is required.");
        return purposeIds;
    }

    /// <param name="sendKey">Distinguishes a re-send (edited booking) from the original appointment mail.</param>
    private async Task TryQueueAppointmentAckAsync(VisitorVisit visit, Visitor visitor, Employee host, string sendKey = "")
    {
        var whenLocal = visit.ExpectedDate.HasValue
            ? visit.ExpectedDate.Value.ToDateTime(visit.ExpectedTime ?? TimeOnly.MinValue)
            : AppTime.Now;
        var ctx = new VisitMailContext(
            VisitorName: visitor.FullName,
            Salutation: SalutationFor(visitor.Gender),
            VisitorEmail: visitor.Email,
            Company: visitor.CompanyName ?? "",
            VisitingPerson: host.FullName,
            VisitingPersonEmail: host.Email,
            VisitorNumber: visit.EntryNumber ?? visitor.VisitorNumber,
            VisitNumber: visit.VisitNumber,
            Location: visit.VisitLocations.Select(l => l.Location?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
            LocationId: visit.VisitLocations.Select(l => (Guid?)l.LocationId).FirstOrDefault(id => id.HasValue && id.Value != Guid.Empty),
            Department: visit.Department?.Name ?? "",
            Purpose: PurposeDisplay.Join(visit.VisitPurposes.Select(p => p.VisitPurpose?.Name), visit.PurposeNotes),
            MeetingMinutes: "",
            MomentLocal: whenLocal);

        // Reload navigation if missing (appointment create may not have included them).
        if (string.IsNullOrWhiteSpace(ctx.Department) || string.IsNullOrWhiteSpace(ctx.Purpose) || string.IsNullOrWhiteSpace(ctx.Location))
        {
            var full = await _db.VisitorVisits.AsNoTracking()
                .Include(v => v.Department)
                .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
                .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
                .FirstOrDefaultAsync(v => v.Id == visit.Id);
            if (full is not null)
            {
                ctx = BuildVisitMailContext(full, whenLocal) with
                {
                    VisitingPerson = host.FullName,
                    VisitingPersonEmail = host.Email,
                    VisitorName = visitor.FullName,
                    Salutation = SalutationFor(visitor.Gender),
                    VisitorEmail = full.IsGroupEmployee ? null : visitor.Email,
                    Company = visitor.CompanyName ?? "",
                    VisitorNumber = visit.EntryNumber ?? visitor.VisitorNumber,
                };
            }
        }

        // "Your appointment at {{Location}}" must not read "at  is confirmed" for a booking without a location.
        if (string.IsNullOrWhiteSpace(ctx.Location))
        {
            var organisation = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == visit.TenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(organisation))
                ctx = ctx with { Location = organisation };
        }

        // Sent off the request path: a slow or failing mail server must not hold the booking form open.
        QueueVisitMail(visit.TenantId, ctx with { AppointmentSendKey = sendKey }, MailContentKind.Appointment);
        var company = string.IsNullOrWhiteSpace(visitor.CompanyName) ? "" : $" ({visitor.CompanyName})";
        QueueHostPush(visit.TenantId, host.UserId,
            sendKey.Length == 0 ? "Appointment booked" : "Appointment updated",
            $"{visitor.FullName}{company} on {whenLocal:dd MMM yyyy, HH:mm}",
            $"appointment-push:{visit.Id}{sendKey}",
            $"/visitors/expected?date={whenLocal:yyyy-MM-dd}&visit={visit.Id}");
    }

    /// <summary>
    /// A visit with no location is hidden from every location-bound login, including the host's own, so a
    /// booking or walk-in made without one takes the host's location.
    /// </summary>
    private async Task AddHostLocationIfNoneAsync(List<Guid> locationIds, Employee host)
    {
        if (locationIds.Count > 0 || host.LocationId is not Guid hostLocation || hostLocation == Guid.Empty) return;
        if (await _db.Locations.AnyAsync(l => l.Id == hostLocation && l.IsActive))
            locationIds.Add(hostLocation);
    }

    private VisitMailContext BuildVisitMailContext(VisitorVisit visit, DateTime momentLocal)
    {
        var locationName = visit.VisitLocations?
            .Select(l => l.Location?.Name)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? "";
        var locationId = visit.VisitLocations?
            .Select(l => (Guid?)l.LocationId)
            .FirstOrDefault(id => id.HasValue && id.Value != Guid.Empty);
        var purpose = visit.VisitPurposes is null
            ? ""
            : PurposeDisplay.Join(visit.VisitPurposes.Select(p => p.VisitPurpose?.Name), visit.PurposeNotes);

        return new VisitMailContext(
            VisitorName: visit.Visitor?.FullName ?? "",
            Salutation: SalutationFor(visit.Visitor?.Gender),
            // Group employees visit on official work: they get no visitor emails.
            VisitorEmail: visit.IsGroupEmployee ? null : visit.Visitor?.Email,
            Company: visit.Visitor?.CompanyName ?? "",
            VisitingPerson: visit.HostEmployee?.FullName ?? "",
            VisitingPersonEmail: visit.HostEmployee?.Email,
            VisitorNumber: visit.EntryNumber ?? visit.Visitor?.VisitorNumber ?? "",
            VisitNumber: visit.VisitNumber,
            Location: locationName,
            LocationId: locationId,
            Department: visit.Department?.Name ?? "",
            Purpose: purpose,
            MeetingMinutes: visit.MeetingMinutes?.Trim() ?? "",
            MomentLocal: momentLocal,
            MeetingHall: visit.MeetingHall?.Name ?? "");
    }

    /// <summary>The minutes email is dated by the meeting (check-in), not by when the minutes were sent.</summary>
    private VisitMailContext BuildMinutesMailContext(VisitorVisit visit, string sendKey)
    {
        var meetingLocal = AsUtc(visit.CheckInAt) is DateTime checkIn
            ? AppTime.ToLocal(checkIn)
            : visit.VisitDate.ToDateTime(visit.VisitTime);
        return BuildVisitMailContext(visit, meetingLocal) with
        {
            MomDecisions = visit.MomDecisions?.Trim() ?? "",
            MomActionItems = FormatActionItems(ReadActionItems(visit.MomActionItemsJson)),
            MomNextMeetingDate = visit.MomNextMeetingDate?.ToString("dd MMM yyyy") ?? "",
            MomSendKey = sendKey,
        };
    }

    private static List<MomActionItem> ReadActionItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<MomActionItem>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string FormatActionItems(IReadOnlyList<MomActionItem> items)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(i + 1).Append(". ").Append(item.Task);
            if (!string.IsNullOrWhiteSpace(item.Owner)) sb.Append(" (Owner: ").Append(item.Owner).Append(')');
            if (item.DueDate is DateOnly due) sb.Append(" (Due: ").Append(due.ToString("dd MMM yyyy")).Append(')');
        }
        return sb.ToString();
    }

    private void QueueVisitMail(Guid tenantId, VisitMailContext ctx, MailContentKind kind)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                var mail = scope.ServiceProvider.GetRequiredService<IVisitMailService>();
                switch (kind)
                {
                    case MailContentKind.CheckIn:
                        await mail.SendCheckInAsync(ctx);
                        break;
                    case MailContentKind.CheckOut:
                        await mail.SendCheckOutAsync(ctx);
                        break;
                    case MailContentKind.Appointment:
                        await mail.SendAppointmentAsync(ctx);
                        break;
                    case MailContentKind.MeetingMinutes:
                        await mail.SendMeetingMinutesAsync(ctx);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background {Kind} email failed for visit {Visit}", kind, ctx.VisitNumber);
            }
        });
    }

    /// <summary>Push to the host's devices off the request path; the push services can be slow to answer.</summary>
    private void QueueHostPush(Guid tenantId, string? userId, string title, string body, string idempotencyKey, string link)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notifications.NotifyAsync("Push", userId, title, body, idempotencyKey, link: link);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background push {Key} failed", idempotencyKey);
            }
        });
    }

    public async Task<PagedResult<VisitorListItemDto>> SearchAsync(VisitorSearchRequest request, ClaimsPrincipal user, int warningMinutes)
    {
        var query = _db.VisitorVisits.AsNoTracking();
        query = await ApplyHostScopeAsync(query, user);
        ApplyFilters(ref query, request);

        var total = await query.CountAsync();
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var pageQuery = ApplySort(query, request.SortBy, request.SortDir)
            .Skip((page - 1) * size)
            .Take(size);

        var items = await ProjectListAsync(pageQuery, warningMinutes);
        return new PagedResult<VisitorListItemDto>(items, total, page, size);
    }

    private static IQueryable<VisitorVisit> ApplySort(IQueryable<VisitorVisit> query, string? sortBy, string? sortDir)
    {
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<VisitorVisit> ordered = (sortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "visitor" => asc ? query.OrderBy(v => v.Visitor.FullName) : query.OrderByDescending(v => v.Visitor.FullName),
            "company" => asc ? query.OrderBy(v => v.Visitor.CompanyName) : query.OrderByDescending(v => v.Visitor.CompanyName),
            "host" => asc ? query.OrderBy(v => v.HostEmployee.FullName) : query.OrderByDescending(v => v.HostEmployee.FullName),
            "date" => asc
                ? query.OrderBy(v => v.VisitDate).ThenBy(v => v.VisitTime)
                : query.OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.VisitTime),
            "status" => asc ? query.OrderBy(v => v.Status) : query.OrderByDescending(v => v.Status),
            "checkout" => asc ? query.OrderBy(v => v.CheckOutAt) : query.OrderByDescending(v => v.CheckOutAt),
            _ => asc ? query.OrderBy(v => v.CreatedAt) : query.OrderByDescending(v => v.CreatedAt),
        };
        // Stable tiebreaker so rows never jump between pages.
        return ordered.ThenByDescending(v => v.CreatedAt).ThenBy(v => v.Id);
    }

    public Task<VisitorDetailDto?> GetAsync(Guid visitId, ClaimsPrincipal user, string encryptionKey) =>
        BuildDetailAsync(visitId, user, encryptionKey);

    private async Task<VisitorDetailDto?> BuildDetailAsync(
        Guid visitId,
        ClaimsPrincipal user,
        string encryptionKey)
    {
        var visit = await DetailVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId);
        if (visit is null) return null;
        await EnsureCanViewVisitAsync(user, visit);

        // Full decrypted ID numbers: SuperAdmin/Admin/Reception only. Security sees masked values.
        var canViewFullId = user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Reception);
        var doc = await _db.VisitorDocuments.AsNoTracking().Include(d => d.IdType)
            .Where(d => d.VisitorVisitId == visitId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        var settings = await _settings.GetAsync();
        var dto = MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
        var feedback = await _feedback.GetForVisitAsync(visitId);
        return new VisitorDetailDto
        {
            VisitId = dto.VisitId,
            VisitorId = dto.VisitorId,
            VisitorNumber = dto.VisitorNumber,
            VisitNumber = dto.VisitNumber,
            VisitorName = dto.VisitorName,
            CompanyName = dto.CompanyName,
            Gender = dto.Gender,
            Salutation = dto.Salutation,
            Phone = dto.Phone,
            Email = dto.Email,
            HostName = dto.HostName,
            DepartmentName = dto.DepartmentName,
            VisitDate = dto.VisitDate,
            VisitTime = dto.VisitTime,
            Status = dto.Status,
            StatusLabel = dto.StatusLabel,
            Purposes = dto.Purposes,
            Locations = dto.Locations,
            PhotoUrl = dto.PhotoUrl,
            CheckInAt = dto.CheckInAt,
            CheckOutAt = dto.CheckOutAt,
            DurationMinutes = dto.DurationMinutes,
            IsLongStay = dto.IsLongStay,
            PreRegistrationReference = dto.PreRegistrationReference,
            PassCode = dto.PassCode,
            NumberOfPersons = dto.NumberOfPersons,
            HasMeetingMinutes = dto.HasMeetingMinutes,
            MomEmailSent = dto.MomEmailSent,
            MomNotRequired = dto.MomNotRequired,
            MomNotRequiredBy = dto.MomNotRequiredBy,
            MomNotRequiredAt = dto.MomNotRequiredAt,
            MeetingHallName = dto.MeetingHallName,
            IsGroupEmployee = dto.IsGroupEmployee,
            HostEmployeeId = visit.HostEmployeeId,
            DepartmentId = visit.DepartmentId,
            PurposeIds = visit.VisitPurposes.Select(p => p.VisitPurposeId).ToList(),
            LocationIds = visit.VisitLocations.Select(l => l.LocationId).ToList(),
            Intercom = visit.HostIntercom,
            PurposeNotes = visit.PurposeNotes,
            PurposeFields = PurposeFields.ToValueDtos(visit.PurposeFieldsJson),
            Notes = visit.Notes,
            MeetingMinutes = visit.MeetingMinutes,
            MomDecisions = visit.MomDecisions,
            MomActionItems = ReadActionItems(visit.MomActionItemsJson)
                .Select(a => new MomActionItemDto { Task = a.Task, Owner = a.Owner, DueDate = a.DueDate })
                .ToList(),
            MomNextMeetingDate = visit.MomNextMeetingDate,
            MomRecordedBy = visit.MomRecordedBy,
            MomRecordedAt = AsUtc(visit.MomRecordedAt),
            MomEmailSentAt = AsUtc(visit.MomEmailSentAt),
            PlantNumber = visit.PlantNumber,
            OtherLocationText = visit.OtherLocationText,
            IdTypeName = doc?.IdType.Name,
            IdNumberMasked = doc?.IdNumberMasked,
            IdNumberFull = canViewFullId && doc is not null ? SensitiveDataHelper.TryDecrypt(doc.IdNumberEncrypted, encryptionKey) : null,
            IdVerificationStatus = doc?.VerificationStatus ?? IdVerificationStatus.NotProvided,
            CheckedInBy = visit.CheckedInByUser?.FullName,
            CheckedOutBy = visit.CheckedOutByUser?.FullName,
            ApprovalHistory = visit.Approvals.OrderByDescending(a => a.CreatedAt).Select(a => new ApprovalHistoryDto
            {
                Id = a.Id,
                Status = a.Status,
                ActionBy = a.ActionByUser?.FullName,
                ActionAt = a.ActionAt,
                RejectionReason = a.RejectionReason,
                CreatedAt = a.CreatedAt
            }).ToList(),
            Feedback = feedback,
        };
    }

    public async Task<VisitorListItemDto> ApproveAsync(Guid visitId, ClaimsPrincipal user)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");
        if (visit.Status != VisitStatus.PendingApproval)
            throw new InvalidOperationException("Visit is not pending approval.");

        EnsureHostAuthorization(visit, user);

        visit.Status = VisitStatus.Approved;
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;
        var approval = visit.Approvals.OrderByDescending(a => a.CreatedAt).FirstOrDefault(a => a.Status == ApprovalStatus.Pending)
            ?? new Approval { VisitorVisitId = visit.Id, HostEmployeeId = visit.HostEmployeeId };
        approval.Status = ApprovalStatus.Approved;
        approval.ActionByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        approval.ActionAt = DateTime.UtcNow;
        if (approval.Id == Guid.Empty) _db.Approvals.Add(approval);
        await SaveVisitLifecycleAsync();
        var settings = await _settings.GetAsync();
        return MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<VisitorListItemDto> RejectAsync(Guid visitId, string reason, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Rejection reason is required.");

        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");
        if (visit.Status != VisitStatus.PendingApproval)
            throw new InvalidOperationException("Visit is not pending approval.");

        EnsureHostAuthorization(visit, user);

        visit.Status = VisitStatus.Rejected;
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;
        var approval = visit.Approvals.OrderByDescending(a => a.CreatedAt).FirstOrDefault(a => a.Status == ApprovalStatus.Pending)
            ?? new Approval { VisitorVisitId = visit.Id, HostEmployeeId = visit.HostEmployeeId };
        approval.Status = ApprovalStatus.Rejected;
        approval.RejectionReason = reason.Trim();
        approval.ActionByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        approval.ActionAt = DateTime.UtcNow;
        if (approval.Id == Guid.Empty) _db.Approvals.Add(approval);
        await SaveVisitLifecycleAsync();
        await _passNumbers.ReleaseHoldAsync(visit.PassNumber);
        var settings = await _settings.GetAsync();
        return MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<PassDto> CheckInAsync(Guid visitId, CheckInRequest request, ClaimsPrincipal user, string webRoot)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        var settings = await _settings.GetAsync();
        EnsureCheckInAllowed(visit, settings);
        await EnsureVisitorNotAlreadyInsideAsync(visit.VisitorId, excludeVisitId: visit.Id);

        var hasPhoto = await _db.VisitorPhotos.AsNoTracking()
            .AnyAsync(p => p.VisitorId == visit.VisitorId && p.IsPrimary);
        if (!hasPhoto && string.IsNullOrWhiteSpace(request.PhotoBase64))
            throw new InvalidOperationException("Visitor face photo is required before check-in.");

        if (!string.IsNullOrWhiteSpace(request.PhotoBase64))
        {
            await SavePhotoAsync(visit.VisitorId, visit.Id, request.PhotoBase64, webRoot, user.Identity?.Name);
            await EnrolFaceAsync(visit.VisitorId, request.PhotoBase64, user);
        }

        var occurredAt = ResolveClientOccurredAt(request.OccurredAt);

        visit.Status = VisitStatus.Inside;
        visit.CheckInAt = occurredAt;
        visit.CheckedInByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.IsInRole(AppRoles.Security))
            visit.SecurityCheckInUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;

        var pass = visit.Passes.FirstOrDefault(p => p.IsActive);
        if (pass is null)
        {
            pass = new VisitorPass
            {
                VisitorVisitId = visit.Id,
                PassCode = ResolvePassCode(visit),
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = occurredAt.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            };
            _db.VisitorPasses.Add(pass);
            visit.Passes.Add(pass);
        }
        pass.PrintCount += 1;
        pass.LastPrintedAt = occurredAt;

        var allocation = await _meetingHalls.AllocateAsync(visit);
        visit.MeetingHallId = allocation.Hall?.Id;
        var lostHalls = new List<Guid>();
        while (true)
        {
            try
            {
                await SaveVisitLifecycleAsync();
                break;
            }
            catch (DbUpdateException ex) when (visit.MeetingHallId is Guid taken && lostHalls.Count < 5 && IsMeetingHallTaken(ex))
            {
                lostHalls.Add(taken);
                allocation = await _meetingHalls.AllocateAsync(visit, lostHalls);
                visit.MeetingHallId = allocation.Hall?.Id;
            }
        }
        if (visit.MeetingHallId.HasValue)
            await _db.Entry(visit).Reference(v => v.MeetingHall).LoadAsync();

        QueueVisitMail(visit.TenantId, BuildVisitMailContext(visit, AppTime.ToLocal(occurredAt)), MailContentKind.CheckIn);
        if (!visit.IsGroupEmployee)
        {
            var hall = visit.MeetingHall is null ? "" : $" Meeting hall: {visit.MeetingHall.Name}.";
            QueueHostPush(visit.TenantId, visit.HostEmployee?.UserId, "Your visitor has arrived",
                $"{visit.Visitor?.FullName} checked in at {AppTime.ToLocal(occurredAt):HH:mm}.{hall}",
                $"checkin-push:{visit.Id}", $"/visitors/{visit.Id}");
        }

        var dto = await BuildPassDto(visit, pass, webRoot);
        dto.MeetingHallWarning = allocation.Warning;
        return dto;
    }

    private static bool IsMeetingHallTaken(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(ApplicationDbContext.MeetingHallInsideIndex, StringComparison.OrdinalIgnoreCase);

    public async Task<VisitorListItemDto> CheckOutAsync(Guid visitId, CheckOutRequest request, ClaimsPrincipal user)
    {
        var visit = await _db.VisitorVisits
            .Include(v => v.Visitor)
            .Include(v => v.Department)
            .Include(v => v.HostEmployee)
            .Include(v => v.Passes)
            .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
            .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
            .Include(v => v.MeetingHall)
            .FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        EnsureCheckOutAllowed(visit);

        if (request.SkipFeedback)
        {
            if (!user.IsInRole(AppRoles.SuperAdmin))
                throw new UnauthorizedAccessException("Only SuperAdmin can perform manual check-out.");
        }
        else
        {
            await _feedback.EnsureFeedbackCompleteAsync(visitId);
        }

        var occurredAt = ResolveClientOccurredAt(request.OccurredAt);
        if (visit.CheckInAt.HasValue && occurredAt < visit.CheckInAt.Value)
            occurredAt = visit.CheckInAt.Value;

        visit.Status = VisitStatus.CheckedOut;
        visit.CheckOutAt = occurredAt;
        visit.IsManualCheckOut = request.SkipFeedback;
        visit.CheckedOutByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;

        foreach (var pass in visit.Passes.Where(p => p.IsActive))
            pass.IsActive = false;

        await SaveVisitLifecycleAsync();
        await _passNumbers.ReleaseHoldAsync(visit.PassNumber);

        QueueVisitMail(visit.TenantId, BuildVisitMailContext(visit, AppTime.ToLocal(occurredAt)), MailContentKind.CheckOut);
        if (visit.MomEmailSentAt is null && !visit.MomNotRequired)
            QueueHostPush(visit.TenantId, visit.HostEmployee?.UserId, "Add minutes of meeting",
                $"{visit.Visitor.FullName} checked out at {AppTime.ToLocal(occurredAt):HH:mm}. Add the minutes of meeting.",
                $"checkout-push:{visit.Id}", $"/visitors/{visit.Id}/minutes");

        var settings = await _settings.GetAsync();
        return MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<int> AutoCheckOutStaleVisitsAsync(TimeSpan maxStay, CancellationToken cancellationToken = default)
    {
        if (maxStay <= TimeSpan.Zero)
            maxStay = TimeSpan.FromHours(24);

        var cutoff = DateTime.UtcNow - maxStay;
        var staleIds = await _db.VisitorVisits.IgnoreQueryFilters()
            .Where(v => v.Status == VisitStatus.Inside
                        && v.CheckInAt != null
                        && v.CheckInAt <= cutoff)
            .OrderBy(v => v.CheckInAt)
            .Select(v => v.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var completed = 0;
        foreach (var visitId in staleIds)
        {
            try
            {
                var visit = await _db.VisitorVisits.IgnoreQueryFilters()
                    .Include(v => v.Visitor)
                    .Include(v => v.Department)
                    .Include(v => v.HostEmployee)
                    .Include(v => v.Passes)
                    .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
                    .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
                    .Include(v => v.MeetingHall)
                    .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

                if (visit is null || visit.Status != VisitStatus.Inside || visit.CheckInAt is null)
                    continue;
                if (visit.CheckInAt > cutoff)
                    continue;

                _tenant.Set(visit.TenantId);

                var occurredAt = DateTime.UtcNow;
                visit.Status = VisitStatus.CheckedOut;
                visit.CheckOutAt = occurredAt;
                visit.IsManualCheckOut = true;
                visit.CheckedOutByUserId = null;
                visit.UpdatedAt = DateTime.UtcNow;
                visit.UpdatedBy = "system:auto-checkout-24h";
                const string autoNote = "Auto check-out after 24 hours without exit.";
                visit.Notes = string.IsNullOrWhiteSpace(visit.Notes)
                    ? autoNote
                    : $"{visit.Notes.Trim()} | {autoNote}";
                if (visit.Notes.Length > 1000)
                    visit.Notes = visit.Notes[..1000];

                foreach (var pass in visit.Passes.Where(p => p.IsActive))
                    pass.IsActive = false;

                await SaveVisitLifecycleAsync();
                await _passNumbers.ReleaseHoldAsync(visit.PassNumber);

                QueueVisitMail(visit.TenantId, BuildVisitMailContext(visit, AppTime.ToLocal(occurredAt)), MailContentKind.CheckOut);
                completed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Auto check-out failed for visit {VisitId}", visitId);
            }
        }

        return completed;
    }

    public async Task<VisitorDetailDto> SaveMeetingMinutesAsync(
        Guid visitId,
        SaveMeetingMinutesRequest request,
        ClaimsPrincipal user)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        if (visit.Status != VisitStatus.Inside && visit.Status != VisitStatus.CheckedOut)
            throw new InvalidOperationException("Meeting minutes can only be entered after the visitor has checked in.");

        EnsureHostAuthorization(visit, user);

        if (visit.MomEmailSentAt.HasValue)
            throw new InvalidOperationException("The MOM email has already been sent. These minutes are read-only.");

        var text = (request.MeetingMinutes ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Discussion points are required.");
        if (text.Length > 4000)
            throw new InvalidOperationException("Discussion points must be at most 4000 characters.");

        var decisions = (request.Decisions ?? "").Trim();
        if (decisions.Length > 4000)
            throw new InvalidOperationException("Decisions must be at most 4000 characters.");

        var actionItems = (request.ActionItems ?? new())
            .Where(a => !string.IsNullOrWhiteSpace(a.Task) || !string.IsNullOrWhiteSpace(a.Owner) || a.DueDate.HasValue)
            .Select(a => new MomActionItem
            {
                Task = (a.Task ?? "").Trim(),
                Owner = string.IsNullOrWhiteSpace(a.Owner) ? null : a.Owner.Trim(),
                DueDate = a.DueDate,
            })
            .ToList();
        if (actionItems.Count > 20)
            throw new InvalidOperationException("Add at most 20 action items.");
        if (actionItems.Any(a => a.Task.Length == 0))
            throw new InvalidOperationException("Each action item needs a task.");
        if (actionItems.Any(a => a.Task.Length > 500 || (a.Owner?.Length ?? 0) > 150))
            throw new InvalidOperationException("Action item tasks must be at most 500 characters and owners at most 150.");

        var now = DateTime.UtcNow;
        visit.MeetingMinutes = text;
        visit.MomDecisions = decisions.Length == 0 ? null : decisions;
        visit.MomActionItemsJson = actionItems.Count == 0 ? null : JsonSerializer.Serialize(actionItems);
        visit.MomNextMeetingDate = request.NextMeetingDate;
        visit.MomRecordedBy = user.Identity?.Name;
        visit.MomRecordedAt = now;
        if (request.SendEmail)
            visit.MomEmailSentAt = now;
        visit.UpdatedAt = now;
        visit.UpdatedBy = user.Identity?.Name;
        await SaveVisitLifecycleAsync();

        if (request.SendEmail)
        {
            var sendKey = $"{visit.Id:N}:{now:yyyyMMddHHmmssfff}";
            QueueVisitMail(visit.TenantId, BuildMinutesMailContext(visit, sendKey), MailContentKind.MeetingMinutes);
        }

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;
    }

    public async Task<VisitorDetailDto> SetMomNotRequiredAsync(Guid visitId, SetMomNotRequiredRequest request, ClaimsPrincipal user)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        if (visit.Status != VisitStatus.Inside && visit.Status != VisitStatus.CheckedOut)
            throw new InvalidOperationException("This can only be set after the visitor has checked in.");

        await EnsureCanViewVisitAsync(user, visit);

        if (request.NotRequired == visit.MomNotRequired)
            return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;

        if (request.NotRequired && !string.IsNullOrWhiteSpace(visit.MeetingMinutes))
            throw new InvalidOperationException("Minutes of meeting have already been saved for this visit.");

        visit.MomNotRequired = request.NotRequired;
        visit.MomNotRequiredBy = request.NotRequired ? user.Identity?.Name : null;
        visit.MomNotRequiredAt = request.NotRequired ? DateTime.UtcNow : null;
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;
        await SaveVisitLifecycleAsync();

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey()))!;
    }

    public async Task<IReadOnlyList<VisitorListItemDto>> GetInsideAsync(int warningMinutes, ClaimsPrincipal? user = null)
    {
        var query = _db.VisitorVisits.AsNoTracking().Where(v => v.Status == VisitStatus.Inside);
        var hostEmployeeIds = user is null ? null : await HostOnlyEmployeeIdsAsync(user);
        if (hostEmployeeIds is not null)
            query = query.Where(v => hostEmployeeIds.Contains(v.HostEmployeeId));
        return await ProjectListAsync(query.OrderBy(v => v.CheckInAt), warningMinutes);
    }

    public async Task<IReadOnlyList<VisitorListItemDto>> GetExpectedAsync(DateOnly? date = null, ClaimsPrincipal? user = null)
    {
        var d = date ?? AppTime.Today;
        var settings = await _settings.GetAsync();
        var query = _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Expected && (v.ExpectedDate == d || v.VisitDate == d));
        var hostEmployeeIds = user is null ? null : await HostOnlyEmployeeIdsAsync(user);
        if (hostEmployeeIds is not null)
            query = query.Where(v => hostEmployeeIds.Contains(v.HostEmployeeId));
        return await ProjectListAsync(query.OrderBy(v => v.ExpectedTime), settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<IReadOnlyList<BookingMatchDto>> FindTodaysBookingsAsync(string? phone, Guid? visitorId)
    {
        var normalizedPhone = TryNormalizePhone(phone);
        if (normalizedPhone is null && visitorId is null) return [];

        var rows = await TodaysBookingsFor(normalizedPhone, visitorId)
            .AsNoTracking()
            .OrderBy(v => v.ExpectedTime)
            .Take(10)
            .Select(v => new
            {
                v.Id,
                v.VisitorId,
                v.VisitNumber,
                v.Visitor.FullName,
                v.Visitor.CompanyName,
                v.Visitor.Gender,
                v.Visitor.Phone,
                v.Visitor.Email,
                v.HostEmployeeId,
                HostName = v.HostEmployee.FullName,
                v.DepartmentId,
                DepartmentName = v.Department.Name,
                v.ExpectedTime,
                v.VisitTime,
                Purposes = v.VisitPurposes.Select(p => new { p.VisitPurposeId, p.VisitPurpose.Name }).ToList(),
                v.PurposeNotes,
                v.NumberOfPersons
            })
            .ToListAsync();

        return rows.Select(v => new BookingMatchDto
        {
            VisitId = v.Id,
            VisitorId = v.VisitorId,
            VisitNumber = v.VisitNumber,
            VisitorName = v.FullName,
            CompanyName = v.CompanyName,
            Gender = v.Gender,
            Phone = v.Phone,
            Email = v.Email,
            HostEmployeeId = v.HostEmployeeId,
            HostName = v.HostName,
            DepartmentId = v.DepartmentId,
            DepartmentName = v.DepartmentName,
            ExpectedTime = v.ExpectedTime ?? v.VisitTime,
            PurposeIds = v.Purposes.Select(p => p.VisitPurposeId).ToList(),
            Purposes = PurposeDisplay.Names(v.Purposes.Select(p => p.Name), v.PurposeNotes),
            PurposeNotes = v.PurposeNotes,
            NumberOfPersons = v.NumberOfPersons
        }).ToList();
    }

    public async Task<VisitorPhoneLookupDto?> LookupByPhoneAsync(string? phone)
    {
        var normalizedPhone = TryNormalizePhone(phone);
        if (normalizedPhone is null) return null;

        var visitorId = await VisitorsByPhone(normalizedPhone).Select(v => (Guid?)v.Id).FirstOrDefaultAsync();
        if (visitorId is null) return null;

        var visitor = await _db.Visitors.AsNoTracking()
            .Where(v => v.Id == visitorId.Value)
            .Select(v => new VisitorPhoneLookupDto
            {
                VisitorId = v.Id,
                VisitorNumber = v.VisitorNumber,
                VisitorName = v.FullName,
                CompanyName = v.CompanyName,
                Gender = v.Gender,
                Phone = v.Phone,
                Email = v.Email,
                TotalVisits = v.Visits.Count(x => x.Status != VisitStatus.Expected && x.Status != VisitStatus.Cancelled),
                LastVisitDate = v.Visits
                    .Where(x => x.CheckInAt != null)
                    .OrderByDescending(x => x.VisitDate)
                    .Select(x => (DateOnly?)x.VisitDate)
                    .FirstOrDefault()
            })
            .FirstAsync();

        var today = AppTime.Today;
        visitor.OpenBookings = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Expected
                        && v.Visitor.Phone == normalizedPhone
                        && (v.ExpectedDate ?? v.VisitDate) >= today)
            .OrderBy(v => v.ExpectedDate ?? v.VisitDate).ThenBy(v => v.ExpectedTime)
            .Take(10)
            .Select(v => new OpenBookingDto
            {
                VisitId = v.Id,
                VisitNumber = v.VisitNumber,
                Date = v.ExpectedDate ?? v.VisitDate,
                Time = v.ExpectedTime ?? v.VisitTime,
                HostEmployeeId = v.HostEmployeeId,
                HostName = v.HostEmployee.FullName
            })
            .ToListAsync();
        return visitor;
    }

    public async Task<IReadOnlyList<PreviousPurposeAnswersDto>> GetPreviousPurposeAnswersAsync(Guid visitorId)
    {
        const int visitsScanned = 30;
        const int maxAnswerSets = 5;
        var visits = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.VisitorId == visitorId && v.PurposeFieldsJson != null
                        && v.Status != VisitStatus.Cancelled && v.Status != VisitStatus.Rejected)
            .OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.CreatedAt)
            .Take(visitsScanned)
            .Select(v => new { v.VisitDate, v.PurposeFieldsJson })
            .ToListAsync();
        if (visits.Count == 0) return [];

        // Only answers to fields that still exist on an active purpose can fill today's form.
        var purposes = await _db.VisitPurposes.AsNoTracking()
            .Where(p => p.IsActive && p.FieldsJson != null)
            .Select(p => new { p.Id, p.Name, p.FieldsJson })
            .ToListAsync();
        var fields = new Dictionary<Guid, (Guid PurposeId, string PurposeName, PurposeField Field)>();
        foreach (var p in purposes)
            foreach (var f in PurposeFields.ReadDefinitions(p.FieldsJson))
                fields[f.Id] = (p.Id, p.Name, f);

        var result = new List<PreviousPurposeAnswersDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var visit in visits)
        {
            var groups = PurposeFields.ReadValues(visit.PurposeFieldsJson)
                .Where(a => fields.ContainsKey(a.FieldId) && !string.IsNullOrWhiteSpace(a.Value))
                .GroupBy(a => fields[a.FieldId].PurposeId);
            foreach (var group in groups)
            {
                var answers = group.ToList();
                // The same picked record is one entry even when its text fields changed (e.g. next year's class).
                var picked = answers.Where(a => a.Verified == true && !string.IsNullOrEmpty(a.RecordId)).ToList();
                var key = picked.Count > 0
                    ? $"{group.Key}|{string.Join("|", picked.OrderBy(a => a.FieldId).Select(a => $"{a.FieldId}#{a.RecordId}"))}"
                    : $"{group.Key}|{string.Join("|", answers.OrderBy(a => a.FieldId).Select(a => $"{a.FieldId}={a.Value.Trim()}"))}";
                if (!seen.Add(key)) continue;

                var owner = fields[answers[0].FieldId];
                result.Add(new PreviousPurposeAnswersDto
                {
                    PurposeId = group.Key,
                    PurposeName = owner.PurposeName,
                    VisitDate = visit.VisitDate,
                    Fields = answers.Select(a =>
                    {
                        var field = fields[a.FieldId].Field;
                        var lookup = PurposeFields.IsLookup(field);
                        var verified = lookup && a.Verified == true && !string.IsNullOrEmpty(a.RecordId);
                        return new PreviousPurposeFieldDto
                        {
                            FieldId = a.FieldId,
                            Label = field.Label,
                            Value = a.Value,
                            LookupSourceId = lookup ? field.LookupSourceId : null,
                            RecordId = verified ? a.RecordId : null,
                            Query = verified ? a.Query : null,
                        };
                    }).ToList(),
                });
                if (result.Count >= maxAnswerSets) return result;
            }
        }
        return result;
    }

    public async Task<LookupRecordVisitorsDto> GetLookupRecordVisitorsAsync(Guid sourceId, string recordId, Guid? visitorId)
    {
        const int maxShown = 8;
        var people = await PeopleWhoCameForRecordAsync(sourceId, recordId);
        if (people.Count == 0) return new LookupRecordVisitorsDto();

        var ids = people.Keys.ToList();
        var visitors = await _db.Visitors.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.FullName,
                v.Phone,
                PhotoPath = v.Photos.Where(p => p.IsPrimary).Select(p => p.FilePath).FirstOrDefault()
            })
            .ToListAsync();
        return new LookupRecordVisitorsDto
        {
            IsNewPerson = visitorId is not Guid id || !people.ContainsKey(id),
            Visitors = visitors
                .Select(v => new LookupRecordVisitorDto
                {
                    VisitorId = v.Id,
                    VisitorName = v.FullName,
                    Phone = v.Phone,
                    PhotoUrl = v.PhotoPath is null ? null : _media.ToPublicApiPath(Path.GetFileName(v.PhotoPath)),
                    LastCheckInAt = AsUtc(people[v.Id].LastCheckIn)!.Value,
                    Visits = people[v.Id].Visits,
                })
                .OrderByDescending(v => v.LastCheckInAt)
                .Take(maxShown)
                .ToList(),
        };
    }

    /// <summary>Visitors who checked in with a verified pick of this record, with their latest check-in and visit count.</summary>
    private async Task<Dictionary<Guid, (DateTime LastCheckIn, int Visits)>> PeopleWhoCameForRecordAsync(Guid sourceId, string recordId)
    {
        var people = new Dictionary<Guid, (DateTime LastCheckIn, int Visits)>();
        recordId = recordId.Trim();
        if (recordId.Length == 0) return people;

        var sourceKey = sourceId.ToString();
        var fieldIds = (await _db.VisitPurposes.AsNoTracking()
                .Where(p => p.FieldsJson != null && p.FieldsJson.Contains(sourceKey))
                .Select(p => p.FieldsJson)
                .ToListAsync())
            .SelectMany(PurposeFields.ReadDefinitions)
            .Where(f => f.LookupSourceId == sourceId)
            .Select(f => f.Id)
            .ToHashSet();
        if (fieldIds.Count == 0) return people;

        // Narrow in SQL by the id as it appears in the stored JSON, then confirm field and record exactly.
        var needle = JsonSerializer.Serialize(recordId)[1..^1];
        var visits = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.CheckInAt != null && v.PurposeFieldsJson != null && v.PurposeFieldsJson.Contains(needle))
            .Select(v => new { v.VisitorId, CheckInAt = v.CheckInAt!.Value, v.PurposeFieldsJson })
            .ToListAsync();
        foreach (var visit in visits)
        {
            var forRecord = PurposeFields.ReadValues(visit.PurposeFieldsJson)
                .Any(a => a.Verified == true && fieldIds.Contains(a.FieldId) && string.Equals(a.RecordId, recordId, StringComparison.Ordinal));
            if (!forRecord) continue;
            people[visit.VisitorId] = people.TryGetValue(visit.VisitorId, out var seen)
                ? (visit.CheckInAt > seen.LastCheckIn ? visit.CheckInAt : seen.LastCheckIn, seen.Visits + 1)
                : (visit.CheckInAt, 1);
        }
        return people;
    }

    /// <summary>Flags picked records (e.g. a student) that others have come for but this visitor never has.</summary>
    private async Task MarkNewPeopleForRecordsAsync(List<PurposeFieldValue> values, IEnumerable<VisitPurpose> purposes, Guid visitorId)
    {
        var sourceByField = purposes
            .SelectMany(p => PurposeFields.ReadDefinitions(p.FieldsJson))
            .Where(PurposeFields.IsLookup)
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => g.First().LookupSourceId!.Value);
        foreach (var value in values)
        {
            if (value.Verified != true || string.IsNullOrEmpty(value.RecordId)
                || !sourceByField.TryGetValue(value.FieldId, out var sourceId)) continue;
            var people = await PeopleWhoCameForRecordAsync(sourceId, value.RecordId);
            if (people.Count > 0 && !people.ContainsKey(visitorId)) value.NewPerson = true;
        }
    }

    /// <summary>
    /// Visitor records with this mobile number, best first: one with a photo (so face matching lands on
    /// the same record), then the one with the most visits.
    /// </summary>
    private IQueryable<Visitor> VisitorsByPhone(string normalizedPhone) =>
        _db.Visitors
            .Where(v => v.Phone == normalizedPhone)
            .OrderByDescending(v => v.Photos.Any())
            .ThenByDescending(v => v.Visits.Count)
            .ThenBy(v => v.CreatedAt);

    /// <summary>Today's still-open bookings for a visitor, matched by mobile number or visitor record.</summary>
    private IQueryable<VisitorVisit> TodaysBookingsFor(string? normalizedPhone, Guid? visitorId)
    {
        var today = AppTime.Today;
        return _db.VisitorVisits
            .Where(v => v.Status == VisitStatus.Expected && (v.ExpectedDate == today || v.VisitDate == today))
            .Where(v => (normalizedPhone != null && v.Visitor.Phone == normalizedPhone)
                        || (visitorId != null && v.VisitorId == visitorId));
    }

    private static string? TryNormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return NormalizeTenDigitPhone(raw);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VisitorListItemDto>> GetPendingApprovalsAsync(ClaimsPrincipal user)
    {
        var query = _db.VisitorVisits.AsNoTracking().Where(v => v.Status == VisitStatus.PendingApproval);
        if (user.IsInRole(AppRoles.Host) && !user.IsInRole(AppRoles.Admin) && !user.IsInRole(AppRoles.SuperAdmin))
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var empIds = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.Id).ToListAsync();
            query = query.Where(v => empIds.Contains(v.HostEmployeeId));
        }
        var settings = await _settings.GetAsync();
        return await ProjectListAsync(query.OrderByDescending(v => v.CreatedAt), settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<PassDto?> GetPassAsync(Guid visitId, string webRoot)
    {
        var visit = await LightVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId);
        if (visit is null) return null;
        var pass = visit.Passes.OrderByDescending(p => p.IssuedAt).FirstOrDefault();
        if (pass is null) return null;
        return await BuildPassDto(visit, pass, webRoot);
    }

    public async Task<PassDto> ReprintPassAsync(Guid visitId, ClaimsPrincipal user, string webRoot)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        if (visit.Status is VisitStatus.Rejected or VisitStatus.Cancelled)
            throw new InvalidOperationException("Pass cannot be issued for a rejected or cancelled visit.");

        var pass = visit.Passes.FirstOrDefault(p => p.IsActive)
                   ?? visit.Passes.OrderByDescending(p => p.IssuedAt).FirstOrDefault();

        if (pass is null)
        {
            var settings = await _settings.GetAsync();
            pass = new VisitorPass
            {
                VisitorVisitId = visit.Id,
                PassCode = ResolvePassCode(visit),
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = DateTime.UtcNow.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            };
            _db.VisitorPasses.Add(pass);
        }

        pass.PrintCount += 1;
        pass.LastPrintedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await BuildPassDto(visit, pass, webRoot);
    }

    public async Task<PassDto?> LookupByVisitNumberAsync(string visitNumber, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(visitNumber)) return null;
        var normalized = visitNumber.Trim();
        var visit = await LightVisitQuery()
            .FirstOrDefaultAsync(v => v.VisitNumber == normalized);
        if (visit is null) return null;
        var pass = visit.Passes.OrderByDescending(p => p.IssuedAt).FirstOrDefault();
        if (pass is null)
        {
            // Historical visits may lack a pass row; still allow verification by visit number.
            pass = new VisitorPass
            {
                Id = Guid.Empty,
                VisitorVisitId = visit.Id,
                PassCode = visit.VisitNumber,
                IssuedAt = visit.CheckInAt ?? visit.CreatedAt,
                ValidUntil = visit.CheckInAt?.AddHours(12) ?? visit.CreatedAt.AddHours(12),
                IsActive = visit.Status == VisitStatus.Inside || visit.Status == VisitStatus.Approved
            };
        }
        return await BuildPassDto(visit, pass, webRoot);
    }

  public async Task<DashboardDto> GetDashboardAsync(ClaimsPrincipal user, int warningMinutes)
    {
        var today = AppTime.Today;
        var fromDate = today.AddDays(-13);
        var todayStartUtc = AppTime.StartOfDayUtc(today);
        var tomorrowStartUtc = AppTime.StartOfDayUtc(today.AddDays(1));

        var hostEmployeeIds = await HostOnlyEmployeeIdsAsync(user);
        var visits = _db.VisitorVisits.AsNoTracking();
        var visitPurposes = _db.VisitorVisitPurposes.AsNoTracking();
        var visitLocations = _db.VisitorVisitLocations.AsNoTracking();
        if (hostEmployeeIds is not null)
        {
            visits = visits.Where(v => hostEmployeeIds.Contains(v.HostEmployeeId));
            visitPurposes = visitPurposes.Where(p => hostEmployeeIds.Contains(p.VisitorVisit.HostEmployeeId));
            visitLocations = visitLocations.Where(l => hostEmployeeIds.Contains(l.VisitorVisit.HostEmployeeId));
        }

        var summary = await visits
            .Where(v => v.VisitDate >= fromDate)
            .Select(v => new
            {
                v.VisitDate,
                v.Status,
                v.CheckOutAt,
                v.IsManualCheckOut,
                v.ExpectedDate,
                DepartmentName = v.Department.Name
            })
            .ToListAsync();

        var todayVisits = summary.Where(v => v.VisitDate == today).ToList();

        // By check-out time, not visit date, so these match the Visitors list "checked out today" filters.
        var checkedOutToday = await visits
            .Where(v => v.Status == VisitStatus.CheckedOut
                && v.CheckOutAt >= todayStartUtc && v.CheckOutAt < tomorrowStartUtc)
            .Select(v => v.IsManualCheckOut)
            .ToListAsync();

        var purposePoints = await visitPurposes
            .Where(p => p.VisitorVisit.VisitDate == today)
            .GroupBy(p => p.VisitPurpose.Name)
            .Select(g => new ChartPointDto { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToListAsync();

        var locationPoints = await visitLocations
            .Where(l => l.VisitorVisit.VisitDate == today)
            .GroupBy(l => l.Location.Name)
            .Select(g => new ChartPointDto { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();

        var recentIds = await visits
            .OrderByDescending(v => v.CreatedAt).Take(8).Select(v => v.Id).ToListAsync();
        var insideIds = await visits
            .Where(v => v.Status == VisitStatus.Inside)
            .OrderBy(v => v.CheckInAt).Take(8).Select(v => v.Id).ToListAsync();

        // Only the counts: the dashboard cards open the Manual check-outs and MOM not sent pages for the lists.
        var withoutMom = visits.Where(v => v.Status == VisitStatus.CheckedOut && v.MomEmailSentAt == null);
        var momPendingCount = await withoutMom.CountAsync(v => !v.MomNotRequired);
        var withoutMomCount = await withoutMom.CountAsync();

        var detailIds = recentIds.Concat(insideIds).Distinct().ToList();
        var detailItems = detailIds.Count == 0
            ? []
            : await ProjectListAsync(_db.VisitorVisits.AsNoTracking().Where(v => detailIds.Contains(v.Id)), warningMinutes);
        var byId = detailItems.ToDictionary(v => v.VisitId);

        VisitorListItemDto MapId(Guid id) =>
            byId.TryGetValue(id, out var visit)
                ? visit
                : new VisitorListItemDto { VisitId = id, VisitorName = "—", StatusLabel = "Unknown" };

        return new DashboardDto
        {
            VisitorsToday = todayVisits.Count(v => v.Status != VisitStatus.Expected),
            CurrentlyInside = summary.Count(v => v.Status == VisitStatus.Inside),
            ExpectedToday = summary.Count(v =>
                v.Status == VisitStatus.Expected && (v.ExpectedDate == today || v.VisitDate == today)),
            PendingApprovals = 0,
            CheckedOutToday = checkedOutToday.Count,
            ManualCheckOutsToday = checkedOutToday.Count(manual => manual),
            ByDepartment = todayVisits.GroupBy(v => v.DepartmentName)
                .Select(g => new ChartPointDto { Label = g.Key, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .ToList(),
            ByPurpose = purposePoints,
            ByLocation = locationPoints,
            DailyTrend = Enumerable.Range(0, 14).Select(i => today.AddDays(-13 + i))
                .Select(d => new ChartPointDto
                {
                    Label = d.ToString("dd MMM"),
                    Value = summary.Count(v => v.VisitDate == d && v.Status != VisitStatus.Expected)
                }).ToList(),
            RecentVisitors = recentIds.Select(MapId).ToList(),
            PendingApprovalItems = [],
            CurrentlyInsideItems = insideIds.Select(MapId).ToList(),
            IsHostView = hostEmployeeIds is not null,
            MomPendingCount = momPendingCount,
            CheckedOutWithoutMomCount = withoutMomCount
        };
    }

    public async Task<IReadOnlyList<MomNotSentByEmployeeDto>> GetMomNotSentByEmployeeAsync(ClaimsPrincipal user)
    {
        var hostEmployeeIds = await HostOnlyEmployeeIdsAsync(user);
        var visits = _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.CheckedOut && v.MomEmailSentAt == null);
        if (hostEmployeeIds is not null)
            visits = visits.Where(v => hostEmployeeIds.Contains(v.HostEmployeeId));
        return await MomNotSentByEmployeeAsync(visits);
    }

    private async Task<List<MomNotSentByEmployeeDto>> MomNotSentByEmployeeAsync(IQueryable<VisitorVisit> withoutMom)
    {
        var groups = await withoutMom
            .GroupBy(v => v.HostEmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                NotRecorded = g.Count(v => !v.MomNotRequired && (v.MeetingMinutes == null || v.MeetingMinutes == "")),
                NotSent = g.Count(v => !v.MomNotRequired && v.MeetingMinutes != null && v.MeetingMinutes != ""),
                NotRequired = g.Count(v => v.MomNotRequired),
                OldestCheckOutAt = g.Min(v => v.MomNotRequired ? null : v.CheckOutAt)
            })
            .ToListAsync();
        if (groups.Count == 0) return [];

        var ids = groups.Select(g => g.EmployeeId).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, DepartmentName = e.Department.Name })
            .ToDictionaryAsync(e => e.Id);

        return groups
            .Select(g => new MomNotSentByEmployeeDto
            {
                EmployeeId = g.EmployeeId,
                EmployeeName = employees.TryGetValue(g.EmployeeId, out var e) ? e.FullName : "Unknown employee",
                DepartmentName = employees.TryGetValue(g.EmployeeId, out var d) ? d.DepartmentName : null,
                NotRecorded = g.NotRecorded,
                NotSent = g.NotSent,
                NotRequired = g.NotRequired,
                OldestCheckOutAt = AsUtc(g.OldestCheckOutAt)
            })
            .OrderByDescending(x => x.NotRecorded + x.NotSent)
            .ThenBy(x => x.OldestCheckOutAt)
            .ThenBy(x => x.EmployeeName)
            .ToList();
    }

    private async Task<List<VisitorListItemDto>> ProjectListAsync(IQueryable<VisitorVisit> query, int warningMinutes)
    {
        var now = DateTime.UtcNow;
        var rows = await query.Select(v => new
        {
            v.Id,
            v.VisitorId,
            VisitorNumber = v.EntryNumber ?? v.Visitor.VisitorNumber,
            v.VisitNumber,
            VisitorName = v.Visitor.FullName,
            CompanyName = v.Visitor.CompanyName,
            Gender = v.Visitor.Gender,
            Phone = v.Visitor.Phone,
            Email = v.Visitor.Email,
            HostName = v.HostEmployee.FullName,
            DepartmentName = v.Department.Name,
            v.VisitDate,
            v.VisitTime,
            v.Status,
            Purposes = v.VisitPurposes.Select(p => p.VisitPurpose.Name).ToList(),
            v.PurposeNotes,
            Locations = v.VisitLocations.Select(l => l.Location.Name).ToList(),
            PhotoPath = v.Visitor.Photos
                .OrderByDescending(p => p.IsPrimary)
                .ThenByDescending(p => p.CreatedAt)
                .Select(p => p.FilePath)
                .FirstOrDefault(),
            v.CheckInAt,
            v.CheckOutAt,
            v.PreRegistrationReference,
            v.NumberOfPersons,
            v.MeetingMinutes,
            v.MomNotRequired,
            v.MomNotRequiredBy,
            v.MomNotRequiredAt,
            v.MomEmailSentAt,
            v.IsManualCheckOut,
            CheckedOutBy = v.CheckedOutByUser != null ? v.CheckedOutByUser.FullName : null,
            v.IsGroupEmployee,
            PassCode = v.Passes.OrderByDescending(p => p.IssuedAt).Select(p => p.PassCode).FirstOrDefault(),
            MeetingHallName = v.MeetingHall != null ? v.MeetingHall.Name : null
        }).ToListAsync();

        return rows.Select(v =>
        {
            int? duration = null;
            var longStay = false;
            if (v.CheckInAt.HasValue)
            {
                var end = v.CheckOutAt ?? now;
                duration = (int)(end - v.CheckInAt.Value).TotalMinutes;
                longStay = v.Status == VisitStatus.Inside && duration >= warningMinutes;
            }

            return new VisitorListItemDto
            {
                VisitId = v.Id,
                VisitorId = v.VisitorId,
                VisitorNumber = v.VisitorNumber,
                VisitNumber = v.VisitNumber,
                VisitorName = v.VisitorName,
                CompanyName = v.CompanyName,
                Gender = v.Gender,
                Salutation = SalutationFor(v.Gender),
                Phone = v.Phone,
                Email = v.Email,
                HostName = v.HostName,
                DepartmentName = v.DepartmentName,
                VisitDate = v.VisitDate,
                VisitTime = v.VisitTime,
                Status = v.Status,
                StatusLabel = v.Status.ToString(),
                Purposes = PurposeDisplay.Names(v.Purposes, v.PurposeNotes),
                Locations = v.Locations,
                PhotoUrl = string.IsNullOrWhiteSpace(v.PhotoPath) ? null : _media.ToPublicApiPath(Path.GetFileName(v.PhotoPath)),
                CheckInAt = AsUtc(v.CheckInAt),
                CheckOutAt = AsUtc(v.CheckOutAt),
                DurationMinutes = duration,
                IsLongStay = longStay,
                PreRegistrationReference = v.PreRegistrationReference,
                PassCode = v.PassCode,
                NumberOfPersons = v.NumberOfPersons < 1 ? 1 : v.NumberOfPersons,
                HasMeetingMinutes = !string.IsNullOrWhiteSpace(v.MeetingMinutes),
                MomEmailSent = v.MomEmailSentAt.HasValue,
                MomNotRequired = v.MomNotRequired,
                MomNotRequiredBy = v.MomNotRequiredBy,
                MomNotRequiredAt = AsUtc(v.MomNotRequiredAt),
                IsManualCheckOut = v.IsManualCheckOut,
                CheckedOutBy = v.CheckedOutBy,
                MeetingHallName = v.MeetingHallName,
                IsGroupEmployee = v.IsGroupEmployee
            };
        }).ToList();
    }

    private IQueryable<VisitorVisit> LightVisitQuery() =>
        _db.VisitorVisits
            .AsNoTracking()
            .Include(v => v.Visitor).ThenInclude(vis => vis.Photos)
            .Include(v => v.Department)
            .Include(v => v.HostEmployee)
            .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
            .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
            .Include(v => v.Passes)
            .Include(v => v.MeetingHall)
            .AsSplitQuery();

    private IQueryable<VisitorVisit> DetailVisitQuery() =>
        LightVisitQuery()
            .Include(v => v.Approvals).ThenInclude(a => a.ActionByUser)
            .Include(v => v.CheckedInByUser)
            .Include(v => v.CheckedOutByUser);

    private IQueryable<VisitorVisit> BaseVisitQuery() =>
        _db.VisitorVisits
            .Include(v => v.Visitor).ThenInclude(vis => vis.Photos)
            .Include(v => v.Department)
            .Include(v => v.HostEmployee)
            .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
            .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
            .Include(v => v.Approvals).ThenInclude(a => a.ActionByUser)
            .Include(v => v.Passes)
            .Include(v => v.CheckedInByUser)
            .Include(v => v.CheckedOutByUser)
            .Include(v => v.MeetingHall)
            .AsSplitQuery();

    private static void ApplyFilters(ref IQueryable<VisitorVisit> query, VisitorSearchRequest request)
    {
        var today = AppTime.Today;
        if (!string.IsNullOrWhiteSpace(request.QuickFilter))
        {
            switch (request.QuickFilter.ToLowerInvariant())
            {
                case "today":
                    query = query.Where(v => v.VisitDate == today); break;
                case "yesterday":
                    query = query.Where(v => v.VisitDate == today.AddDays(-1)); break;
                case "thisweek":
                    var start = today.AddDays(-(int)today.DayOfWeek);
                    query = query.Where(v => v.VisitDate >= start && v.VisitDate <= today); break;
                case "thismonth":
                    var monthStart = new DateOnly(today.Year, today.Month, 1);
                    query = query.Where(v => v.VisitDate >= monthStart && v.VisitDate <= today); break;
                case "approved":
                    query = query.Where(v => v.Status == VisitStatus.Approved); break;
                case "inside":
                    query = query.Where(v => v.Status == VisitStatus.Inside); break;
                case "checkedouttoday":
                case "manualcheckouttoday":
                    var dayStartUtc = AppTime.StartOfDayUtc(today);
                    var nextDayStartUtc = AppTime.StartOfDayUtc(today.AddDays(1));
                    query = query.Where(v => v.Status == VisitStatus.CheckedOut
                        && v.CheckOutAt >= dayStartUtc && v.CheckOutAt < nextDayStartUtc);
                    if (request.QuickFilter.Equals("manualcheckouttoday", StringComparison.OrdinalIgnoreCase))
                        query = query.Where(v => v.IsManualCheckOut);
                    break;
                case "manualcheckout":
                    query = query.Where(v => v.IsManualCheckOut); break;
                case "momnotsent":
                    query = query.Where(v => v.Status == VisitStatus.CheckedOut
                        && !v.MomNotRequired && v.MomEmailSentAt == null); break;
                case "momnotrequired":
                    query = query.Where(v => v.Status == VisitStatus.CheckedOut
                        && v.MomNotRequired && v.MomEmailSentAt == null); break;
            }
        }

        if (request.DateFrom.HasValue) query = query.Where(v => v.VisitDate >= request.DateFrom);
        if (request.DateTo.HasValue) query = query.Where(v => v.VisitDate <= request.DateTo);
        if (request.DepartmentId.HasValue) query = query.Where(v => v.DepartmentId == request.DepartmentId);
        if (request.Status.HasValue) query = query.Where(v => v.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.VisitorName))
            query = query.Where(v => v.Visitor.FullName.Contains(request.VisitorName));
        if (!string.IsNullOrWhiteSpace(request.Company))
            query = query.Where(v => v.Visitor.CompanyName.Contains(request.Company));
        if (!string.IsNullOrWhiteSpace(request.Phone))
            query = query.Where(v => v.Visitor.Phone != null && v.Visitor.Phone.Contains(request.Phone));
        if (!string.IsNullOrWhiteSpace(request.VisitorNumber))
            query = query.Where(v => v.VisitNumber.Contains(request.VisitorNumber)
                || v.Visitor.VisitorNumber.Contains(request.VisitorNumber)
                || (v.EntryNumber != null && v.EntryNumber.Contains(request.VisitorNumber)));
        if (!string.IsNullOrWhiteSpace(request.Host))
            query = query.Where(v => v.HostEmployee.FullName.Contains(request.Host));
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = request.Query.Trim();
            query = query.Where(v =>
                v.Visitor.FullName.Contains(q) ||
                v.Visitor.CompanyName.Contains(q) ||
                (v.Visitor.Phone != null && v.Visitor.Phone.Contains(q)) ||
                v.VisitNumber.Contains(q) ||
                v.Visitor.VisitorNumber.Contains(q) ||
                (v.EntryNumber != null && v.EntryNumber.Contains(q)) ||
                v.HostEmployee.FullName.Contains(q) ||
                (v.PreRegistrationReference != null && v.PreRegistrationReference.Contains(q)));
        }
    }

    private VisitorListItemDto MapListItem(VisitorVisit visit, int warningMinutes)
    {
        int? duration = null;
        var longStay = false;
        if (visit.CheckInAt.HasValue)
        {
            var end = visit.CheckOutAt ?? DateTime.UtcNow;
            duration = (int)(end - visit.CheckInAt.Value).TotalMinutes;
            longStay = visit.Status == VisitStatus.Inside && duration >= warningMinutes;
        }

        var photo = visit.Visitor.Photos.OrderByDescending(p => p.CreatedAt).FirstOrDefault(p => p.IsPrimary)
                    ?? visit.Visitor.Photos.OrderByDescending(p => p.CreatedAt).FirstOrDefault();

        return new VisitorListItemDto
        {
            VisitId = visit.Id,
            VisitorId = visit.VisitorId,
            VisitorNumber = visit.EntryNumber ?? visit.Visitor.VisitorNumber,
            VisitNumber = visit.VisitNumber,
            VisitorName = visit.Visitor.FullName,
            CompanyName = visit.Visitor.CompanyName,
            Gender = visit.Visitor.Gender,
            Salutation = SalutationFor(visit.Visitor.Gender),
            Phone = visit.Visitor.Phone,
            Email = visit.Visitor.Email,
            HostName = visit.HostEmployee.FullName,
            DepartmentName = visit.Department.Name,
            VisitDate = visit.VisitDate,
            VisitTime = visit.VisitTime,
            Status = visit.Status,
            StatusLabel = visit.Status.ToString(),
            Purposes = PurposeDisplay.Names(visit.VisitPurposes.Select(p => p.VisitPurpose.Name), visit.PurposeNotes),
            Locations = visit.VisitLocations.Select(l => l.Location.Name).ToList(),
            PhotoUrl = photo is null ? null : _media.ToPublicApiPath(Path.GetFileName(photo.FilePath)),
            CheckInAt = AsUtc(visit.CheckInAt),
            CheckOutAt = AsUtc(visit.CheckOutAt),
            DurationMinutes = duration,
            IsLongStay = longStay,
            PreRegistrationReference = visit.PreRegistrationReference,
            PassCode = visit.Passes.OrderByDescending(p => p.IssuedAt).FirstOrDefault()?.PassCode,
            NumberOfPersons = visit.NumberOfPersons < 1 ? 1 : visit.NumberOfPersons,
            HasMeetingMinutes = !string.IsNullOrWhiteSpace(visit.MeetingMinutes),
            MomEmailSent = visit.MomEmailSentAt.HasValue,
            MomNotRequired = visit.MomNotRequired,
            MomNotRequiredBy = visit.MomNotRequiredBy,
            MomNotRequiredAt = AsUtc(visit.MomNotRequiredAt),
            IsManualCheckOut = visit.IsManualCheckOut,
            CheckedOutBy = visit.CheckedOutByUser?.FullName,
            MeetingHallName = visit.MeetingHall?.Name,
            IsGroupEmployee = visit.IsGroupEmployee
        };
    }

    private async Task<PassDto> BuildPassDto(VisitorVisit visit, VisitorPass pass, string webRoot)
    {
        var settings = await _settings.GetAsync();
        var item = MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
        var brandLocationId = visit.VisitLocations.Select(l => (Guid?)l.LocationId).FirstOrDefault();
        var brand = brandLocationId is null ? settings : await _settings.GetAsync(brandLocationId);
        return new PassDto
        {
            PassId = pass.Id,
            VisitId = visit.Id,
            PassCode = pass.PassCode,
            VisitorName = item.VisitorName,
            CompanyName = item.CompanyName,
            HostName = item.HostName,
            DepartmentName = item.DepartmentName,
            Purposes = item.Purposes,
            PurposeFields = PurposeFields.ToValueDtos(visit.PurposeFieldsJson),
            Locations = item.Locations,
            VisitNumber = item.VisitNumber,
            CheckInAt = item.CheckInAt,
            PhotoUrl = item.PhotoUrl,
            Status = item.StatusLabel,
            BrandCompanyName = brand.CompanyName,
            BrandLogoPath = brand.LogoPath,
            MeetingHallName = visit.MeetingHall?.Name,
            MeetingHallIntercom = visit.MeetingHall?.IntercomNo
        };
    }

    /// <summary>
    /// Finds the closest stored face template within the caller's tenant.
    /// Templates are streamed rather than materialised so large tenants stay bounded in memory.
    /// </summary>
    public async Task<FaceSearchMatchDto?> FaceSearchAsync(string photoBase64, ClaimsPrincipal user)
    {
        _ = ResolveTenantId(user);

        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face recognition is not configured on this server.");

        // Embedding happens here, from the uploaded image. The client never supplies a template, so a
        // caller cannot craft a vector that matches an arbitrary visitor.
        var probe = EmbedPhoto(photoBase64);

        Guid? bestVisitorId = null;
        var bestSimilarity = double.MinValue;

        var candidates = _db.VisitorFaceDescriptors.AsNoTracking()
            .Where(f => f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new { f.VisitorId, f.Descriptor })
            .AsAsyncEnumerable();

        await foreach (var candidate in candidates)
        {
            var stored = FromBytes(candidate.Descriptor);
            if (stored.Length != probe.Vector.Length) continue;

            var similarity = _faces.Similarity(probe.Vector, stored);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestVisitorId = candidate.VisitorId;
            }
        }

        if (bestVisitorId is null || bestSimilarity < _faces.MatchThreshold)
        {
            return null;
        }

        var visitor = await _db.Visitors.AsNoTracking()
            .Where(v => v.Id == bestVisitorId.Value)
            .Select(v => new
            {
                v.Id,
                v.VisitorNumber,
                v.FullName,
                v.CompanyName,
                v.Gender,
                v.Phone,
                v.Email,
                TotalVisits = v.Visits.Count,
                LastVisitDate = v.Visits.OrderByDescending(x => x.VisitDate).Select(x => (DateOnly?)x.VisitDate).FirstOrDefault(),
                PhotoPath = v.Photos.Where(p => p.IsPrimary).Select(p => p.FilePath).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        // Tenant query filter removed the visitor: treat as no match rather than leaking existence.
        if (visitor is null) return null;

        var currentlyInside = await _db.VisitorVisits.AsNoTracking()
            .AnyAsync(v => v.VisitorId == visitor.Id && v.Status == VisitStatus.Inside);

        return new FaceSearchMatchDto
        {
            VisitorId = visitor.Id,
            VisitorNumber = visitor.VisitorNumber,
            VisitorName = visitor.FullName,
            CompanyName = visitor.CompanyName,
            Gender = visitor.Gender,
            Phone = visitor.Phone,
            Email = visitor.Email,
            PhotoUrl = visitor.PhotoPath is null ? null : _media.ToPublicApiPath(Path.GetFileName(visitor.PhotoPath)),
            LastVisitDate = visitor.LastVisitDate,
            TotalVisits = visitor.TotalVisits,
            Similarity = Math.Round(bestSimilarity, 4),
            IsCurrentlyInside = currentlyInside
        };
    }

    /// <summary>
    /// Recognises a visitor who is currently inside, for face-driven check-out.
    /// </summary>
    /// <remarks>
    /// Restricted to visitors with an open visit, which is both the correct behaviour and a much
    /// smaller candidate set than the whole tenant. Tenant and site filters still apply through the
    /// query, so a site-bound guard only ever matches people inside their own site.
    /// </remarks>
    public async Task<FaceCheckoutMatchDto?> FaceIdentifyInsideAsync(string photoBase64, ClaimsPrincipal user)
    {
        _ = ResolveTenantId(user);

        if (!_faces.IsAvailable)
            throw new InvalidOperationException("Face recognition is not configured on this server.");

        var probe = EmbedPhoto(photoBase64);

        var candidates = await (
            from template in _db.VisitorFaceDescriptors.AsNoTracking()
            join visit in _db.VisitorVisits.AsNoTracking()
                on template.VisitorId equals visit.VisitorId
            where template.Model == _faces.ModelId
                  && template.Dimensions == _faces.Dimensions
                  && visit.Status == VisitStatus.Inside
            select new { visit.Id, template.VisitorId, template.Descriptor })
            .ToListAsync();

        Guid? bestVisitId = null;
        var bestSimilarity = double.MinValue;

        foreach (var candidate in candidates)
        {
            var stored = FromBytes(candidate.Descriptor);
            if (stored.Length != probe.Vector.Length) continue;

            var similarity = _faces.Similarity(probe.Vector, stored);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestVisitId = candidate.Id;
            }
        }

        if (bestVisitId is null || bestSimilarity < _faces.MatchThreshold)
        {
            return null;
        }

        var matchRow = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Id == bestVisitId.Value)
            .Select(v => new
            {
                VisitId = v.Id,
                VisitorId = v.VisitorId,
                VisitorName = v.Visitor.FullName,
                Gender = v.Visitor.Gender,
                CompanyName = v.Visitor.CompanyName,
                VisitNumber = v.VisitNumber,
                CheckInAt = v.CheckInAt,
                LocationId = v.VisitLocations.Select(l => (Guid?)l.LocationId).FirstOrDefault(),
                PhotoUrl = v.Visitor.Photos.Where(p => p.IsPrimary).Select(p => p.FilePath).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (matchRow is null) return null;

        var match = new FaceCheckoutMatchDto
        {
            VisitId = matchRow.VisitId,
            VisitorId = matchRow.VisitorId,
            VisitorName = matchRow.VisitorName,
            Salutation = SalutationFor(matchRow.Gender),
            CompanyName = matchRow.CompanyName,
            VisitNumber = matchRow.VisitNumber,
            LocationId = matchRow.LocationId,
            CheckInAt = matchRow.CheckInAt,
            PhotoUrl = matchRow.PhotoUrl,
            Similarity = Math.Round(bestSimilarity, 4)
        };
        if (match.PhotoUrl is not null)
            match.PhotoUrl = _media.ToPublicApiPath(Path.GetFileName(match.PhotoUrl));


        return match;
    }

    /// <summary>
    /// Extracts a face template from the registration photo. Enrolment is best-effort: a photo the
    /// detector cannot use must not block the visitor from being registered.
    /// </summary>
    private async Task EnrolFaceAsync(Guid visitorId, string photoBase64, ClaimsPrincipal user)
    {
        if (!_faces.IsAvailable) return;

        FaceEmbedding embedding;
        try
        {
            embedding = EmbedPhoto(photoBase64);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("Face enrolment skipped for visitor {VisitorId}: {Reason}", visitorId, ex.Message);
            return;
        }

        await SaveFaceDescriptorAsync(visitorId, embedding.Vector, user);
    }

    private FaceEmbedding EmbedPhoto(string photoBase64)
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
            return _faces.Embed(bytes);
        }
        catch (FaceEmbeddingException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
    }

    private async Task SaveFaceDescriptorAsync(Guid visitorId, float[] descriptor, ClaimsPrincipal user)
    {
        if (descriptor.Length != _faces.Dimensions) return;

        // Keep a rolling window of recent captures: matching against several poses is far more reliable
        // than against the newest one alone. Only the overflow is discarded.
        var stale = await _db.VisitorFaceDescriptors
            .Where(f => f.VisitorId == visitorId && f.Model == _faces.ModelId)
            .OrderByDescending(f => f.CreatedAt)
            .Skip(FaceRecognition.MaxTemplatesPerVisitor - 1)
            .ToListAsync();
        _db.VisitorFaceDescriptors.RemoveRange(stale);

        _db.VisitorFaceDescriptors.Add(new VisitorFaceDescriptor
        {
            TenantId = ResolveTenantId(user),
            VisitorId = visitorId,
            Descriptor = ToBytes(descriptor),
            Dimensions = descriptor.Length,
            Model = _faces.ModelId,
            CreatedBy = user.Identity?.Name
        });
        await _db.SaveChangesAsync();
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

    private async Task SavePhotoAsync(Guid visitorId, Guid visitId, string base64, string webRoot, string? userName)
    {
        _ = webRoot;
        var raw = base64.Contains(',') ? base64.Split(',')[1] : base64;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Photo data is invalid.");
        }

        // Exactly-one-face gate BEFORE any permanent media write.
        _faces.EnsureExactlyOneFace(bytes);

        var fileName = await _media.SaveVisitorPhotoAsync(visitorId, bytes);
        _ = _media.IsAllowedImage(bytes, out var contentType);

        var existing = await _db.VisitorPhotos.Where(p => p.VisitorId == visitorId && p.IsPrimary).ToListAsync();
        foreach (var p in existing) p.IsPrimary = false;

        _db.VisitorPhotos.Add(new VisitorPhoto
        {
            VisitorId = visitorId,
            VisitorVisitId = visitId,
            FilePath = fileName,
            ContentType = contentType,
            FileSizeBytes = bytes.Length,
            IsPrimary = true,
            CreatedBy = userName
        });
        await _db.SaveChangesAsync();
    }

    public FacePhotoValidationDto ValidateVisitorPhoto(string photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            throw new InvalidOperationException(FacePhotoRules.NoFaceMessage);

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

        if (!_media.IsAllowedImage(bytes, out _))
            throw new InvalidOperationException("Photo must be a valid JPEG, PNG, or WebP image under 5MB.");

        var faceCount = _faces.CountFaces(bytes);
        if (faceCount != 1)
            throw new InvalidOperationException(FacePhotoRules.MessageForCount(faceCount));

        return new FacePhotoValidationDto
        {
            FaceCount = faceCount,
            Accepted = true,
            Status = "Ready to capture"
        };
    }

    private async Task<IQueryable<VisitorVisit>> ApplyHostScopeAsync(IQueryable<VisitorVisit> query, ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin)
            || user.IsInRole(AppRoles.Reception) || user.IsInRole(AppRoles.Security))
            return query;

        if (!user.IsInRole(AppRoles.Host))
            throw new UnauthorizedAccessException("Not authorized to search visitors.");

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var empIds = await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.Id).ToListAsync();
        return query.Where(v => empIds.Contains(v.HostEmployeeId));
    }

    /// <summary>
    /// Employee ids linked to a Host-only login, or null when the user may see every visitor
    /// (SuperAdmin, Admin, Reception, Security).
    /// </summary>
    private async Task<List<Guid>?> HostOnlyEmployeeIdsAsync(ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin)
            || user.IsInRole(AppRoles.Reception) || user.IsInRole(AppRoles.Security)
            || !user.IsInRole(AppRoles.Host))
            return null;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return await _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => e.Id).ToListAsync();
    }

    private Guid ResolveTenantId(ClaimsPrincipal user) => TenantClaims.RequireTenantId(user, _tenant);

    private async Task EnsureCanViewVisitAsync(ClaimsPrincipal user, VisitorVisit visit)
    {
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin)
            || user.IsInRole(AppRoles.Reception) || user.IsInRole(AppRoles.Security))
            return;

        if (!user.IsInRole(AppRoles.Host))
            throw new UnauthorizedAccessException("Not authorized to view this visitor.");

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isHost = await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.Id == visit.HostEmployeeId);
        if (!isHost)
            throw new UnauthorizedAccessException("Hosts may only view their own visitors.");
    }

    /// <summary>
    /// Persists Visit lifecycle mutations under the SQL Server rowversion token.
    /// A stale concurrent update becomes <see cref="ConcurrencyConflictException"/> (HTTP 409),
    /// not an unhandled 500 / opaque DbUpdateException.
    /// </summary>
    private async Task SaveVisitLifecycleAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    /// <summary>
    /// Prefer browser/device time when the client sends OccurredAt; otherwise fall back to server UTC.
    /// Unspecified Kind is treated as UTC (JSON ISO-Z). Local Kind is converted to UTC.
    /// </summary>
    internal static DateTime ResolveClientOccurredAt(DateTime? occurredAt)
    {
        if (occurredAt is null)
            return DateTime.UtcNow;

        var value = occurredAt.Value;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    /// <summary>SQL DateTime often comes back Unspecified; mark as UTC so JSON emits Z and clients convert correctly.</summary>
    internal static DateTime? AsUtc(DateTime? value)
    {
        if (value is null) return null;
        var v = value.Value;
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }

    /// <summary>
    /// Server-side gate for REGISTERED/EXPECTED/APPROVED -> INSIDE. Approval is tenant configuration,
    /// so an expected visit may only skip approval when the tenant has approval turned off.
    /// </summary>
    internal static void EnsureCheckInAllowed(VisitorVisit visit, SettingsDto settings)
    {
        switch (visit.Status)
        {
            case VisitStatus.Inside:
                throw new InvalidOperationException("Visitor is already checked in.");
            case VisitStatus.CheckedOut:
                throw new InvalidOperationException("VISITOR ALREADY CHECKED OUT");
            case VisitStatus.Rejected:
                throw new InvalidOperationException("This visit was rejected and cannot be checked in.");
            case VisitStatus.Cancelled:
                throw new InvalidOperationException("This visit was cancelled and cannot be checked in.");
            case VisitStatus.PendingApproval:
                throw new InvalidOperationException("Visitor is awaiting host approval and cannot be checked in yet.");
            case VisitStatus.Expected when settings.ApprovalRequired:
                throw new InvalidOperationException("Visitor must be approved before check-in.");
            case VisitStatus.Expected:
            case VisitStatus.Approved:
                return;
            default:
                throw new InvalidOperationException("Visitor must be approved before check-in.");
        }
    }

    /// <summary>Server-side gate for INSIDE -> CHECKED_OUT. Only a visitor currently inside can be checked out.</summary>
    internal static void EnsureCheckOutAllowed(VisitorVisit visit)
    {
        if (visit.Status == VisitStatus.CheckedOut)
            throw new InvalidOperationException("VISITOR ALREADY CHECKED OUT");
        if (visit.Status != VisitStatus.Inside)
            throw new InvalidOperationException("Visitor is not currently inside.");
    }

    private async Task EnsureVisitorNotAlreadyInsideAsync(Guid visitorId, Guid? excludeVisitId = null)
    {
        var alreadyInside = await _db.VisitorVisits.AsNoTracking()
            .AnyAsync(v =>
                v.VisitorId == visitorId
                && v.Status == VisitStatus.Inside
                && (excludeVisitId == null || v.Id != excludeVisitId.Value));
        if (alreadyInside)
            throw new InvalidOperationException(
                "This visitor is already checked in. Check them out before registering or checking in again.");
    }

    /// <summary>Best face match in the tenant, or null when below threshold / unavailable.</summary>
    private async Task<Guid?> TryFindMatchingVisitorIdAsync(string photoBase64)
    {
        try
        {
            var probe = EmbedPhoto(photoBase64);
            Guid? bestVisitorId = null;
            var bestSimilarity = double.MinValue;

            var candidates = _db.VisitorFaceDescriptors.AsNoTracking()
                .Where(f => f.Model == _faces.ModelId && f.Dimensions == _faces.Dimensions)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new { f.VisitorId, f.Descriptor })
                .AsAsyncEnumerable();

            await foreach (var candidate in candidates)
            {
                var stored = FromBytes(candidate.Descriptor);
                if (stored.Length != probe.Vector.Length) continue;

                var similarity = _faces.Similarity(probe.Vector, stored);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestVisitorId = candidate.VisitorId;
                }
            }

            if (bestVisitorId is null || bestSimilarity < _faces.MatchThreshold)
                return null;
            return bestVisitorId;
        }
        catch (InvalidOperationException)
        {
            // No face / multiple faces — let normal photo validation handle messaging elsewhere.
            return null;
        }
    }

    private static string DailyNumberPrefix(string prefix, DateOnly date) => $"{prefix}-V-{date:yyyyMMdd}-";

    /// <summary>
    /// Visitor numbers and visit entry numbers share one daily sequence, so a returning visitor's
    /// entry on a given day never repeats a number a new visitor got that day.
    /// </summary>
    private async Task<int> MaxDailyNumberAsync(string patternPrefix)
    {
        var latestVisitor = await _db.Visitors.AsNoTracking()
            .Where(v => v.VisitorNumber.StartsWith(patternPrefix))
            .OrderByDescending(v => v.VisitorNumber)
            .Select(v => v.VisitorNumber)
            .FirstOrDefaultAsync();
        var tenantId = _tenant.TenantId;
        var latestEntry = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.EntryNumber != null && v.EntryNumber.StartsWith(patternPrefix))
            .OrderByDescending(v => v.EntryNumber)
            .Select(v => v.EntryNumber!)
            .FirstOrDefaultAsync();
        var values = new[] { latestVisitor, latestEntry }.Where(v => v is not null).Select(v => v!);
        return MaxNumericSuffix(values, patternPrefix);
    }

    private async Task<string> NextVisitorNumberAsync(string prefix)
    {
        var patternPrefix = DailyNumberPrefix(prefix, AppTime.Today);
        var next = await MaxDailyNumberAsync(patternPrefix) + 1;
        return $"{patternPrefix}{next:D4}";
    }

    private async Task<string> NextEntryNumberAsync(string prefix, DateOnly visitDate)
    {
        var patternPrefix = DailyNumberPrefix(prefix, visitDate);
        var next = await MaxDailyNumberAsync(patternPrefix) + 1;
        return $"{patternPrefix}{next:D4}";
    }

    /// <summary>
    /// Human-readable commercial visit number, e.g. VMS-2026-000184. The sequence is per tenant, so this
    /// deliberately steps around the site filter: a site-bound operator must not restart at 1 and collide
    /// with a number another site already issued.
    /// </summary>
    private async Task<string> NextVisitNumberAsync(string prefix)
    {
        var tenantId = RequireTenantIdForNumbering();
        var patternPrefix = VisitNumberPrefix(prefix);
        // Fixed-width suffixes sort identically as text and as numbers, so the highest existing
        // number is one indexed row rather than the whole year loaded into memory.
        var latest = await _db.VisitorVisits.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.VisitNumber.StartsWith(patternPrefix))
            .OrderByDescending(v => v.VisitNumber)
            .Select(v => v.VisitNumber)
            .FirstOrDefaultAsync();
        var next = latest is null ? 1 : MaxNumericSuffix(new[] { latest }, patternPrefix) + 1;
        return $"{patternPrefix}{next:D6}";
    }

    /// <summary>
    /// Number allocation reads across every site in the tenant, so it can only run with a known tenant.
    /// </summary>
    private Guid RequireTenantIdForNumbering() =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required to allocate a visit number.");

    private static string VisitNumberPrefix(string prefix)
    {
        var year = AppTime.Now.ToString("yyyy");
        var code = string.IsNullOrWhiteSpace(prefix) ? "VMS" : prefix.Trim().ToUpperInvariant();
        return $"{code}-{year}-";
    }

    /// <summary>
    /// Prefer the allocated pass number; fall back to VisitNumber for legacy visits.
    /// Never invent a random code that could collide with a reused desk badge.
    /// </summary>
    private static string ResolvePassCode(VisitorVisit visit) =>
        !string.IsNullOrWhiteSpace(visit.PassNumber)
            ? visit.PassNumber.Trim()
            : visit.VisitNumber;

    /// <summary>
    /// Persists a new visit, re-issuing the visit number if a concurrent registration claimed it first.
    /// The (TenantId, VisitNumber) unique index is the source of truth; this only removes the operator-visible failure.
    /// </summary>
    /// <summary>
    /// Persists a new visit with its human-readable numbers allocated under a per-tenant lock.
    /// "Read the highest number, add one" is only safe if concurrent registrations cannot read the
    /// same highest number, so allocation and insert happen inside one transaction holding that lock.
    /// The unique indexes stay the final authority; the retry covers numbers created outside this path.
    /// </summary>
    private async Task SaveNewVisitAsync(VisitorVisit visit, Visitor? newVisitor, string prefix, bool keepVisitNumber = false)
    {
        var dailyPrefix = DailyNumberPrefix(prefix, visit.VisitDate);
        var keepEntryNumber = keepVisitNumber
            && visit.EntryNumber?.StartsWith(dailyPrefix, StringComparison.Ordinal) == true;
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
                    await AcquireNumberAllocationLockAsync(visit.TenantId, prefix);

                    if (!keepVisitNumber)
                        visit.VisitNumber = await NextVisitNumberAsync(prefix);
                    if (newVisitor is not null)
                        newVisitor.VisitorNumber = await NextVisitorNumberAsync(prefix);
                    if (!keepEntryNumber)
                        visit.EntryNumber = newVisitor is not null
                            && newVisitor.VisitorNumber.StartsWith(dailyPrefix, StringComparison.Ordinal)
                                ? newVisitor.VisitorNumber
                                : await NextEntryNumberAsync(prefix, visit.VisitDate);

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsDuplicateNumber(ex))
            {
                // Fall through and allocate again on the next attempt.
            }
        }
    }

    /// <summary>
    /// Serialises number allocation for one tenant and year via ADO.NET <c>sp_getapplock</c>.
    /// The lock is owned by the surrounding EF transaction connection. Providers without
    /// application locks fall back to the unique index plus retry.
    /// </summary>
    private async Task AcquireNumberAllocationLockAsync(Guid tenantId, string prefix)
    {
        if (!_db.Database.IsSqlServer()) return;

        var resource = $"vms-number:{tenantId}:{VisitNumberPrefix(prefix)}";
        var connection = (Microsoft.Data.SqlClient.SqlConnection)_db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var transaction = (Microsoft.Data.SqlClient.SqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            "EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000";
        cmd.Parameters.Add(AdoSql.NVarChar("@Resource", resource, 255));
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool IsDuplicateNumber(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_VisitorVisits_TenantId_VisitNumber", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_Visitors_TenantId_VisitorNumber", StringComparison.OrdinalIgnoreCase);
    }

    private static int MaxNumericSuffix(IEnumerable<string> values, string patternPrefix)
    {
        var max = 0;
        foreach (var value in values)
        {
            if (value.Length <= patternPrefix.Length) continue;
            if (int.TryParse(value.AsSpan(patternPrefix.Length), out var n) && n > max)
                max = n;
        }
        return max;
    }

    private void ValidateRegistration(RegisterVisitorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorName))
            throw new InvalidOperationException("Visitor name is required.");
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            throw new InvalidOperationException("Company name is required.");
        if (request.Gender is not (VisitorGender.Male or VisitorGender.Female))
            throw new InvalidOperationException("Gender is required (Male or Female).");
        if (request.DepartmentId == Guid.Empty)
            throw new InvalidOperationException("Department is required.");
        if (!request.HostEmployeeId.HasValue && string.IsNullOrWhiteSpace(request.HostName))
            throw new InvalidOperationException("Person to meet is required.");
        if (request.PurposeIds.Count == 0 && string.IsNullOrWhiteSpace(request.PurposeNotes))
            throw new InvalidOperationException("Purpose of visit is required.");
        if (!string.IsNullOrWhiteSpace(request.Telephone))
            request.Telephone = NormalizeTenDigitPhone(request.Telephone);

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new InvalidOperationException("Email is required.");
        var email = request.Email.Trim();
        if (email.Length > 150)
            throw new InvalidOperationException("Email address is too long (maximum 150 characters).");
        if (!new EmailAddressAttribute().IsValid(email))
            throw new InvalidOperationException("Email address format is invalid.");
        request.Email = email;

        if (!string.IsNullOrWhiteSpace(request.IdNumber) && request.IdNumber.Trim().Length > 40)
            throw new InvalidOperationException("ID number is too long (maximum 40 characters).");
        if (request.NumberOfPersons < 1 || request.NumberOfPersons > 99)
            throw new InvalidOperationException("Number of persons must be between 1 and 99.");
    }

    private static readonly string[] StaticIdTypeNames = ["Aadhaar", "Pan Card", "Passport"];

    /// <summary>
    /// Resolves an optional ID proof to a tenant IdTypes row, creating the static type if needed.
    /// </summary>
    private async Task<Guid> ResolveOptionalIdTypeAsync(RegisterVisitorRequest request)
    {
        string? name = null;
        if (!string.IsNullOrWhiteSpace(request.IdTypeName))
        {
            name = request.IdTypeName.Trim();
            var match = StaticIdTypeNames.FirstOrDefault(n =>
                string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException("ID type must be Aadhaar, Pan Card, or Passport.");
            name = match;
        }
        else if (request.IdTypeId is Guid existingId && existingId != Guid.Empty)
        {
            var byId = await _db.IdTypes.FirstOrDefaultAsync(t => t.Id == existingId && t.IsActive)
                ?? throw new InvalidOperationException("Selected ID type was not found.");
            var match = StaticIdTypeNames.FirstOrDefault(n =>
                string.Equals(n, byId.Name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException("ID type must be Aadhaar, Pan Card, or Passport.");
            return byId.Id;
        }
        else
        {
            throw new InvalidOperationException("ID type is required when an ID number is provided.");
        }

        var existing = await _db.IdTypes.FirstOrDefaultAsync(t =>
            t.IsActive && t.Name == name);
        if (existing is not null) return existing.Id;

        var created = new IdType
        {
            TenantId = _tenant.TenantId is Guid tid && tid != Guid.Empty
                ? tid
                : throw new UnauthorizedAccessException("Tenant context is required."),
            Name = name,
            IsActive = true,
            SortOrder = Array.IndexOf(StaticIdTypeNames, name),
            CreatedBy = "system"
        };
        _db.IdTypes.Add(created);
        await _db.SaveChangesAsync();
        return created.Id;
    }

    private async Task<Employee> ResolveHostAsync(Guid departmentId, Guid? hostEmployeeId, string? hostName, string? createdBy)
    {
        var departmentExists = await _db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive);
        if (!departmentExists)
            throw new InvalidOperationException("Selected department was not found.");

        if (hostEmployeeId.HasValue && hostEmployeeId.Value != Guid.Empty)
        {
            var byId = await _db.Employees.Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.Id == hostEmployeeId.Value && e.IsActive)
                ?? throw new InvalidOperationException("Selected host was not found.");
            if (_tenant.LocationId is Guid scope && byId.LocationId.HasValue && byId.LocationId.Value != scope)
                throw new InvalidOperationException("Selected host belongs to another location.");
            if (byId.DepartmentId != departmentId)
                throw new InvalidOperationException("Host does not belong to the selected department.");
            return byId;
        }

        var name = hostName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Person to meet is required.");

        var locationScope = _tenant.LocationId;
        var existing = await _db.Employees.Include(e => e.Department)
            .FirstOrDefaultAsync(e =>
                e.DepartmentId == departmentId &&
                e.IsActive &&
                (locationScope == null || e.LocationId == null || e.LocationId == locationScope) &&
                e.FullName.ToLower() == name.ToLower());

        if (existing is not null)
            return existing;

        var created = new Employee
        {
            TenantId = TenantClaims.RequireTenantId(null, _tenant),
            FullName = name,
            DepartmentId = departmentId,
            LocationId = locationScope,
            IsActive = true,
            CreatedBy = createdBy
        };
        _db.Employees.Add(created);
        await _db.SaveChangesAsync();
        await _db.Entry(created).Reference(e => e.Department).LoadAsync();
        return created;
    }

    private void EnsureHostAuthorization(VisitorVisit visit, ClaimsPrincipal user)
    {
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin)) return;
        if (!user.IsInRole(AppRoles.Host))
            throw new UnauthorizedAccessException("Not authorized to approve visitors.");
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var linked = _db.Employees.Any(e => e.UserId == userId && e.Id == visit.HostEmployeeId);
        if (!linked)
            throw new UnauthorizedAccessException("You can only act on visitors assigned to you.");
    }

    /// <summary>
    /// Strips non-digits and optional 91 / leading 0 country prefixes; requires exactly 10 digits.
    /// </summary>
    internal static string NormalizeTenDigitPhone(string raw)
    {
        var digits = Regex.Replace(raw.Trim(), @"\D", "");
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith('0'))
            digits = digits[1..];
        if (!TenDigitPhone.IsMatch(digits))
            throw new InvalidOperationException("Phone number must be exactly 10 digits.");
        return digits;
    }

    /// <summary>
    /// Updates a returning visitor from desk registration. New columns (e.g. Gender) can be filled
    /// on the next visit; blank phone/email do not wipe values already on file.
    /// </summary>
    private static void ApplyReturningVisitorProfile(Visitor visitor, RegisterVisitorRequest request, string? updatedBy)
    {
        visitor.FullName = request.VisitorName.Trim();
        visitor.CompanyName = request.CompanyName.Trim();
        visitor.Gender = request.Gender;
        if (!string.IsNullOrWhiteSpace(request.Telephone))
            visitor.Phone = NormalizeTenDigitPhone(request.Telephone);
        if (!string.IsNullOrWhiteSpace(request.Email))
            visitor.Email = request.Email.Trim();
        visitor.UpdatedAt = DateTime.UtcNow;
        visitor.UpdatedBy = updatedBy;
    }

    /// <summary>Male → Mr., Female → Ms. Empty when gender is unknown (legacy rows).</summary>
    internal static string SalutationFor(VisitorGender? gender) => gender switch
    {
        VisitorGender.Male => "Mr.",
        VisitorGender.Female => "Ms.",
        _ => ""
    };
}
