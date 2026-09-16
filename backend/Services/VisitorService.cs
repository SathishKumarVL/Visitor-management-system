using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public interface IVisitorService
{
    Task<VisitorDetailDto> RegisterAsync(RegisterVisitorRequest request, ClaimsPrincipal user, string webRoot);
    Task<VisitorDetailDto> CreateExpectedAsync(ExpectedVisitorRequest request, ClaimsPrincipal user);
    Task<PagedResult<VisitorListItemDto>> SearchAsync(VisitorSearchRequest request, ClaimsPrincipal user, int warningMinutes);
    Task<VisitorDetailDto?> GetAsync(Guid visitId, ClaimsPrincipal user, string encryptionKey);
    Task<VisitorListItemDto> ApproveAsync(Guid visitId, ClaimsPrincipal user);
    Task<VisitorListItemDto> RejectAsync(Guid visitId, string reason, ClaimsPrincipal user);
    Task<PassDto> CheckInAsync(Guid visitId, CheckInRequest request, ClaimsPrincipal user, string webRoot);
    Task<VisitorListItemDto> CheckOutAsync(Guid visitId, CheckOutRequest request, ClaimsPrincipal user);
    Task<IReadOnlyList<VisitorListItemDto>> GetInsideAsync(int warningMinutes);
    Task<IReadOnlyList<VisitorListItemDto>> GetExpectedAsync(DateOnly? date = null);
    Task<IReadOnlyList<VisitorListItemDto>> GetPendingApprovalsAsync(ClaimsPrincipal user);
    Task<PassDto?> GetPassAsync(Guid visitId, string webRoot);
    Task<PassDto> ReprintPassAsync(Guid visitId, ClaimsPrincipal user, string webRoot);
    Task<PassDto?> LookupByVisitNumberAsync(string visitNumber, string webRoot);
    Task<DashboardDto> GetDashboardAsync(ClaimsPrincipal user, int warningMinutes);
    Task<FaceSearchMatchDto?> FaceSearchAsync(float[] descriptor, ClaimsPrincipal user);
}

public class VisitorService : IVisitorService
{
    private readonly ApplicationDbContext _db;
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VisitorService> _logger;
    private readonly IMediaStorageService _media;
    private readonly ITenantContext _tenant;
    private readonly ISiteService _sites;
    private static readonly Regex IndianPhone = new(@"^(\+91[\-\s]?)?[6-9]\d{9}$|^0\d{2,4}[\-\s]?\d{6,8}$", RegexOptions.Compiled);

    public VisitorService(
        ApplicationDbContext db,
        ISettingsService settings,
        IAuditService audit,
        INotificationService notifications,
        IConfiguration config,
        IHostEnvironment env,
        IServiceScopeFactory scopeFactory,
        ILogger<VisitorService> logger,
        IMediaStorageService media,
        ITenantContext tenant,
        ISiteService sites)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
        _notifications = notifications;
        _config = config;
        _env = env;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _media = media;
        _tenant = tenant;
        _sites = sites;
    }

    private string EncryptionKey() => SecretConfiguration.GetRequiredEncryptionKey(_config, _env);

    public async Task<VisitorDetailDto> RegisterAsync(RegisterVisitorRequest request, ClaimsPrincipal user, string webRoot)
    {
        ValidateRegistration(request);
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
        if (settings.IdVerificationRequired && (request.IdTypeId is null || string.IsNullOrWhiteSpace(request.IdNumber)))
            throw new InvalidOperationException("Identity verification is required.");

        var host = await ResolveHostAsync(request.DepartmentId, request.HostEmployeeId, request.HostName, user.Identity?.Name);

        var purposes = await _db.VisitPurposes.Where(p => request.PurposeIds.Contains(p.Id) && p.IsActive).ToListAsync();
        if (purposes.Count == 0) throw new InvalidOperationException("At least one valid purpose is required.");

        var locationIds = request.LocationIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        var locations = locationIds.Count == 0
            ? []
            : await _db.Locations.Where(l => locationIds.Contains(l.Id) && l.IsActive).ToListAsync();
        if (locationIds.Count > 0 && locations.Count == 0)
            throw new InvalidOperationException("At least one valid location is required.");
        if (locations.Any(l => l.RequiresPlantNumber) && string.IsNullOrWhiteSpace(request.PlantNumber))
            throw new InvalidOperationException("Plant number is required when Plant No. is selected.");
        if (locations.Any(l => l.RequiresOtherText) && string.IsNullOrWhiteSpace(request.OtherLocationText))
            throw new InvalidOperationException("Please specify the other location.");

        Visitor visitor;
        Visitor? newVisitor = null;
        if (request.ExpectedVisitId.HasValue)
        {
            var expected = await _db.VisitorVisits.Include(v => v.Visitor)
                .FirstOrDefaultAsync(v => v.Id == request.ExpectedVisitId.Value)
                ?? throw new InvalidOperationException("Expected visitor record not found.");
            visitor = expected.Visitor;
            visitor.FullName = request.VisitorName.Trim();
            visitor.CompanyName = request.CompanyName.Trim();
            visitor.Phone = request.Telephone?.Trim();
            visitor.Email = request.Email?.Trim();
            visitor.UpdatedAt = DateTime.UtcNow;
            visitor.UpdatedBy = user.Identity?.Name;
            // The pre-registration is superseded, not erased: its visit number and audit trail stay intact.
            expected.Status = VisitStatus.Cancelled;
            expected.Notes = string.IsNullOrWhiteSpace(expected.Notes)
                ? "Superseded by arrival registration."
                : $"{expected.Notes} | Superseded by arrival registration.";
            expected.UpdatedAt = DateTime.UtcNow;
            expected.UpdatedBy = user.Identity?.Name;
        }
        else
        {
            visitor = new Visitor
            {
                TenantId = ResolveTenantId(user),
                VisitorNumber = await NextVisitorNumberAsync(settings.VisitorIdPrefix),
                FullName = request.VisitorName.Trim(),
                CompanyName = request.CompanyName.Trim(),
                Phone = request.Telephone?.Trim(),
                Email = request.Email?.Trim(),
                CreatedBy = user.Identity?.Name
            };
            _db.Visitors.Add(visitor);
            newVisitor = visitor;
        }

        // Approval is tenant configuration: walk-ins and scheduled visits can be gated independently.
        var needsApproval = request.IsWalkIn ? settings.WalkInApprovalRequired : settings.ApprovalRequired;
        var visit = new VisitorVisit
        {
            TenantId = ResolveTenantId(user),
            SiteId = await _sites.ResolveVisitSiteIdAsync(),
            Visitor = visitor,
            VisitNumber = await NextVisitNumberAsync(settings.VisitorIdPrefix),
            VisitorType = request.IsWalkIn ? VisitorType.WalkIn : VisitorType.Expected,
            Status = needsApproval ? VisitStatus.PendingApproval : VisitStatus.Approved,
            VisitDate = request.VisitDate ?? DateOnly.FromDateTime(DateTime.Now),
            VisitTime = request.VisitTime ?? TimeOnly.FromDateTime(DateTime.Now),
            DepartmentId = request.DepartmentId,
            HostEmployeeId = host.Id,
            HostIntercom = null,
            PlantNumber = request.PlantNumber,
            OtherLocationText = request.OtherLocationText,
            PurposeNotes = request.PurposeNotes,
            Notes = request.Notes,
            NumberOfPersons = request.NumberOfPersons < 1 ? 1 : Math.Min(request.NumberOfPersons, 99),
            IsWalkIn = request.IsWalkIn,
            CreatedBy = user.Identity?.Name
        };

        foreach (var p in purposes)
            visit.VisitPurposes.Add(new VisitorVisitPurpose { VisitPurposeId = p.Id });
        foreach (var l in locations)
            visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = l.Id });

        if (needsApproval)
        {
            visit.Approvals.Add(new Approval
            {
                Status = ApprovalStatus.Pending,
                HostEmployeeId = host.Id,
                RequestedToUserId = host.UserId
            });
        }
        else
        {
            visit.Approvals.Add(new Approval
            {
                Status = ApprovalStatus.Approved,
                HostEmployeeId = host.Id,
                ActionByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ActionAt = DateTime.UtcNow,
                Notes = "Auto-approved based on settings"
            });
        }

        _db.VisitorVisits.Add(visit);
        await SaveNewVisitAsync(visit, newVisitor, settings.VisitorIdPrefix);

        if (!string.IsNullOrWhiteSpace(request.PhotoBase64))
            await SavePhotoAsync(visitor.Id, visit.Id, request.PhotoBase64, webRoot, user.Identity?.Name);

        if (request.FaceDescriptor is { Length: > 0 })
            await SaveFaceDescriptorAsync(visitor.Id, request.FaceDescriptor, user);

        if (request.IdTypeId.HasValue && !string.IsNullOrWhiteSpace(request.IdNumber))
        {
            var key = SecretConfiguration.GetRequiredEncryptionKey(_config, _env);
            _db.VisitorDocuments.Add(new VisitorDocument
            {
                VisitorId = visitor.Id,
                VisitorVisitId = visit.Id,
                IdTypeId = request.IdTypeId.Value,
                IdNumberEncrypted = SensitiveDataHelper.Encrypt(request.IdNumber.Trim(), key),
                IdNumberMasked = SensitiveDataHelper.MaskId(request.IdNumber.Trim()),
                VerificationStatus = IdVerificationStatus.Verified,
                CreatedBy = user.Identity?.Name
            });
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("VisitorCreated", "VisitorVisit", visit.Id.ToString(),
            $"Visitor {visitor.FullName} registered ({visit.VisitNumber})");

        // A pass is only valid for a visit that is cleared to enter; pending-approval visits get one at check-in.
        if (visit.Status == VisitStatus.Approved)
        {
            _db.VisitorPasses.Add(new VisitorPass
            {
                VisitorVisitId = visit.Id,
                PassCode = $"PASS-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = DateTime.UtcNow.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            });
            await _db.SaveChangesAsync();
        }

        if (needsApproval)
        {
            var recipient = host.Email ?? host.UserId ?? "host";
            await _notifications.NotifyAsync("Internal", recipient, "Visitor approval required",
                $"Visitor {visitor.FullName} from {visitor.CompanyName} is pending your approval.");
        }

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey(), includeAuditHistory: false))!;
    }

    public async Task<VisitorDetailDto> CreateExpectedAsync(ExpectedVisitorRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorName) || string.IsNullOrWhiteSpace(request.CompanyName))
            throw new InvalidOperationException("Visitor name and company are required.");

        var settings = await _settings.GetAsync();
        var visitor = new Visitor
        {
            TenantId = ResolveTenantId(user),
            VisitorNumber = await NextVisitorNumberAsync(settings.VisitorIdPrefix),
            FullName = request.VisitorName.Trim(),
            CompanyName = request.CompanyName.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            CreatedBy = user.Identity?.Name
        };
        _db.Visitors.Add(visitor);

        var visit = new VisitorVisit
        {
            TenantId = ResolveTenantId(user),
            SiteId = await _sites.ResolveVisitSiteIdAsync(),
            Visitor = visitor,
            VisitNumber = await NextVisitNumberAsync(settings.VisitorIdPrefix),
            PreRegistrationReference = $"EXP-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            VisitorType = VisitorType.PreRegistered,
            Status = VisitStatus.Expected,
            VisitDate = request.ExpectedDate,
            VisitTime = request.ExpectedTime,
            ExpectedDate = request.ExpectedDate,
            ExpectedTime = request.ExpectedTime,
            DepartmentId = request.DepartmentId,
            HostEmployeeId = request.HostEmployeeId,
            PlantNumber = request.PlantNumber,
            OtherLocationText = request.OtherLocationText,
            Notes = request.Notes,
            CreatedBy = user.Identity?.Name
        };

        foreach (var pid in request.PurposeIds.Distinct())
            visit.VisitPurposes.Add(new VisitorVisitPurpose { VisitPurposeId = pid });
        foreach (var lid in request.LocationIds.Distinct())
            visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = lid });

        _db.VisitorVisits.Add(visit);
        await SaveNewVisitAsync(visit, visitor, settings.VisitorIdPrefix);
        await _audit.LogAsync("ExpectedVisitorCreated", "VisitorVisit", visit.Id.ToString(),
            $"Expected visitor {visitor.FullName} ({visit.PreRegistrationReference})");

        return (await BuildDetailAsync(visit.Id, user, EncryptionKey(), includeAuditHistory: false))!;
    }

    public async Task<PagedResult<VisitorListItemDto>> SearchAsync(VisitorSearchRequest request, ClaimsPrincipal user, int warningMinutes)
    {
        var query = _db.VisitorVisits.AsNoTracking();
        query = await ApplyHostScopeAsync(query, user);
        ApplyFilters(ref query, request);

        var total = await query.CountAsync();
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var pageQuery = query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size);

        var items = await ProjectListAsync(pageQuery, warningMinutes);
        return new PagedResult<VisitorListItemDto>(items, total, page, size);
    }

    public Task<VisitorDetailDto?> GetAsync(Guid visitId, ClaimsPrincipal user, string encryptionKey) =>
        BuildDetailAsync(visitId, user, encryptionKey, includeAuditHistory: true);

    private async Task<VisitorDetailDto?> BuildDetailAsync(
        Guid visitId,
        ClaimsPrincipal user,
        string encryptionKey,
        bool includeAuditHistory)
    {
        var visit = await DetailVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId);
        if (visit is null) return null;
        await EnsureCanViewVisitAsync(user, visit);

        // Full decrypted ID numbers: SuperAdmin/Admin/Reception only. Security sees masked values.
        var canViewFullId = user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Reception);
        var doc = await _db.VisitorDocuments.AsNoTracking().Include(d => d.IdType)
            .Where(d => d.VisitorVisitId == visitId || d.VisitorId == visit.VisitorId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        List<AuditLogDto> audits = [];
        if (includeAuditHistory)
        {
            audits = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityId == visitId.ToString() || a.EntityId == visit.VisitorId.ToString())
                .OrderByDescending(a => a.CreatedAt)
                .Take(50)
                .Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    Action = a.Action,
                    Entity = a.Entity,
                    EntityId = a.EntityId,
                    UserName = a.UserName,
                    Description = a.Description,
                    IpAddress = a.IpAddress,
                    CreatedAt = a.CreatedAt
                }).ToListAsync();
        }

        var settings = await _settings.GetAsync();
        var dto = MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
        return new VisitorDetailDto
        {
            VisitId = dto.VisitId,
            VisitorId = dto.VisitorId,
            VisitorNumber = dto.VisitorNumber,
            VisitNumber = dto.VisitNumber,
            VisitorName = dto.VisitorName,
            CompanyName = dto.CompanyName,
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
            Intercom = visit.HostIntercom,
            PurposeNotes = visit.PurposeNotes,
            Notes = visit.Notes,
            PlantNumber = visit.PlantNumber,
            OtherLocationText = visit.OtherLocationText,
            IdTypeName = doc?.IdType.Name,
            IdNumberMasked = doc?.IdNumberMasked,
            IdNumberFull = canViewFullId && doc is not null ? SensitiveDataHelper.Decrypt(doc.IdNumberEncrypted, encryptionKey) : null,
            IdVerificationStatus = doc?.VerificationStatus ?? IdVerificationStatus.NotProvided,
            EntryGate = visit.EntryGate?.Name,
            ExitGate = visit.ExitGate?.Name,
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
            AuditHistory = audits
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
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VisitorApproved", "VisitorVisit", visit.Id.ToString(), $"Approved visitor {visit.Visitor.FullName}");
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
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VisitorRejected", "VisitorVisit", visit.Id.ToString(), $"Rejected: {reason}");
        var settings = await _settings.GetAsync();
        return MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<PassDto> CheckInAsync(Guid visitId, CheckInRequest request, ClaimsPrincipal user, string webRoot)
    {
        var visit = await BaseVisitQuery().FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        var settings = await _settings.GetAsync();
        EnsureCheckInAllowed(visit, settings);

        var gate = request.EntryGateId.HasValue
            ? await _db.EntryGates.FirstOrDefaultAsync(g => g.Id == request.EntryGateId)
            : await _db.EntryGates.FirstOrDefaultAsync(g => g.IsDefault && g.IsActive)
              ?? await _db.EntryGates.FirstOrDefaultAsync(g => g.IsActive);

        visit.Status = VisitStatus.Inside;
        visit.CheckInAt = DateTime.UtcNow;
        visit.EntryGateId = gate?.Id;
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
                PassCode = $"PASS-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = DateTime.UtcNow.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            };
            _db.VisitorPasses.Add(pass);
            visit.Passes.Add(pass);
        }
        pass.PrintCount += 1;
        pass.LastPrintedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("VisitorCheckedIn", "VisitorVisit", visit.Id.ToString(), $"Checked in {visit.Visitor.FullName}");
        await _audit.LogAsync("VisitorPassPrinted", "VisitorPass", pass.Id.ToString(), $"Pass issued {pass.PassCode}");

        return await BuildPassDto(visit, pass, webRoot);
    }

    public async Task<VisitorListItemDto> CheckOutAsync(Guid visitId, CheckOutRequest request, ClaimsPrincipal user)
    {
        var visit = await _db.VisitorVisits
            .Include(v => v.Visitor)
            .Include(v => v.Department)
            .Include(v => v.HostEmployee)
            .Include(v => v.Passes)
            .Include(v => v.VisitPurposes).ThenInclude(p => p.VisitPurpose)
            .Include(v => v.VisitLocations).ThenInclude(l => l.Location)
            .FirstOrDefaultAsync(v => v.Id == visitId)
            ?? throw new InvalidOperationException("Visit not found.");

        EnsureCheckOutAllowed(visit);

        var gate = request.ExitGateId.HasValue
            ? await _db.ExitGates.AsNoTracking().FirstOrDefaultAsync(g => g.Id == request.ExitGateId)
            : await _db.ExitGates.AsNoTracking().FirstOrDefaultAsync(g => g.IsDefault && g.IsActive)
              ?? await _db.ExitGates.AsNoTracking().FirstOrDefaultAsync(g => g.IsActive);

        visit.Status = VisitStatus.CheckedOut;
        visit.CheckOutAt = DateTime.UtcNow;
        visit.ExitGateId = gate?.Id;
        visit.CheckedOutByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        visit.UpdatedAt = DateTime.UtcNow;
        visit.UpdatedBy = user.Identity?.Name;

        foreach (var pass in visit.Passes.Where(p => p.IsActive))
            pass.IsActive = false;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("VisitorCheckedOut", "VisitorVisit", visit.Id.ToString(), $"Checked out {visit.Visitor.FullName}");

        var visitorName = visit.Visitor.FullName;
        var visitorEmail = visit.Visitor.Email;
        var visitMomentLocal = visit.CheckInAt.HasValue
            ? visit.CheckInAt.Value.ToLocalTime()
            : DateTime.Now;

        // Send thank-you mail in the background so checkout never waits on SMTP.
        // The tenant is captured here and re-seeded into the background scope: that scope has no
        // request to resolve it from, and an unseeded scope must never fall back to another tenant.
        var notificationTenantId = visit.TenantId;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(notificationTenantId);
                var mail = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await mail.SendVisitorThankYouEmailAsync(visitorName, visitorEmail, visitMomentLocal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background thank-you email failed for {Visitor}", visitorName);
            }
        });

        var settings = await _settings.GetAsync();
        return MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
    }

    public async Task<IReadOnlyList<VisitorListItemDto>> GetInsideAsync(int warningMinutes)
    {
        return await ProjectListAsync(
            _db.VisitorVisits.AsNoTracking().Where(v => v.Status == VisitStatus.Inside).OrderBy(v => v.CheckInAt),
            warningMinutes);
    }

    public async Task<IReadOnlyList<VisitorListItemDto>> GetExpectedAsync(DateOnly? date = null)
    {
        var d = date ?? DateOnly.FromDateTime(DateTime.Now);
        var settings = await _settings.GetAsync();
        return await ProjectListAsync(
            _db.VisitorVisits.AsNoTracking()
                .Where(v => v.Status == VisitStatus.Expected && (v.ExpectedDate == d || v.VisitDate == d))
                .OrderBy(v => v.ExpectedTime),
            settings.MaxVisitDurationWarningMinutes);
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
                PassCode = $"PASS-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                IssuedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                ValidUntil = DateTime.UtcNow.AddHours(settings.VisitorPassValidityHours),
                IsActive = true
            };
            _db.VisitorPasses.Add(pass);
            await _audit.LogAsync("VisitorPassIssued", "VisitorPass", pass.Id.ToString(), $"Pass issued {pass.PassCode}");
        }

        pass.PrintCount += 1;
        pass.LastPrintedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VisitorPassPrinted", "VisitorPass", pass.Id.ToString(), $"Pass printed {pass.PassCode}");
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
        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromDate = today.AddDays(-13);
        var utcToday = DateTime.UtcNow.Date;

        var summary = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.VisitDate >= fromDate)
            .Select(v => new
            {
                v.VisitDate,
                v.Status,
                v.CheckOutAt,
                v.ExpectedDate,
                DepartmentName = v.Department.Name
            })
            .ToListAsync();

        var todayVisits = summary.Where(v => v.VisitDate == today).ToList();

        var purposePoints = await _db.VisitorVisitPurposes.AsNoTracking()
            .Where(p => p.VisitorVisit.VisitDate == today)
            .GroupBy(p => p.VisitPurpose.Name)
            .Select(g => new ChartPointDto { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToListAsync();

        var locationPoints = await _db.VisitorVisitLocations.AsNoTracking()
            .Where(l => l.VisitorVisit.VisitDate == today)
            .GroupBy(l => l.Location.Name)
            .Select(g => new ChartPointDto { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();

        var recentIds = await _db.VisitorVisits.AsNoTracking()
            .OrderByDescending(v => v.CreatedAt).Take(8).Select(v => v.Id).ToListAsync();
        var insideIds = await _db.VisitorVisits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Inside)
            .OrderBy(v => v.CheckInAt).Take(8).Select(v => v.Id).ToListAsync();

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
            CheckedOutToday = summary.Count(v =>
                v.Status == VisitStatus.CheckedOut &&
                v.CheckOutAt.HasValue &&
                v.CheckOutAt.Value.Date == utcToday),
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
            CurrentlyInsideItems = insideIds.Select(MapId).ToList()
        };
    }

    private async Task<List<VisitorListItemDto>> ProjectListAsync(IQueryable<VisitorVisit> query, int warningMinutes)
    {
        var now = DateTime.UtcNow;
        var rows = await query.Select(v => new
        {
            v.Id,
            v.VisitorId,
            VisitorNumber = v.Visitor.VisitorNumber,
            v.VisitNumber,
            VisitorName = v.Visitor.FullName,
            CompanyName = v.Visitor.CompanyName,
            Phone = v.Visitor.Phone,
            Email = v.Visitor.Email,
            HostName = v.HostEmployee.FullName,
            DepartmentName = v.Department.Name,
            v.VisitDate,
            v.VisitTime,
            v.Status,
            Purposes = v.VisitPurposes.Select(p => p.VisitPurpose.Name).ToList(),
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
            PassCode = v.Passes.OrderByDescending(p => p.IssuedAt).Select(p => p.PassCode).FirstOrDefault()
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
                Phone = v.Phone,
                Email = v.Email,
                HostName = v.HostName,
                DepartmentName = v.DepartmentName,
                VisitDate = v.VisitDate,
                VisitTime = v.VisitTime,
                Status = v.Status,
                StatusLabel = v.Status.ToString(),
                Purposes = v.Purposes,
                Locations = v.Locations,
                PhotoUrl = string.IsNullOrWhiteSpace(v.PhotoPath) ? null : _media.ToPublicApiPath(Path.GetFileName(v.PhotoPath)),
                CheckInAt = v.CheckInAt,
                CheckOutAt = v.CheckOutAt,
                DurationMinutes = duration,
                IsLongStay = longStay,
                PreRegistrationReference = v.PreRegistrationReference,
                PassCode = v.PassCode,
                NumberOfPersons = v.NumberOfPersons < 1 ? 1 : v.NumberOfPersons
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
            .AsSplitQuery();

    private IQueryable<VisitorVisit> DetailVisitQuery() =>
        LightVisitQuery()
            .Include(v => v.Approvals).ThenInclude(a => a.ActionByUser)
            .Include(v => v.EntryGate)
            .Include(v => v.ExitGate)
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
            .Include(v => v.EntryGate)
            .Include(v => v.ExitGate)
            .Include(v => v.CheckedInByUser)
            .Include(v => v.CheckedOutByUser)
            .AsSplitQuery();

    private static void ApplyFilters(ref IQueryable<VisitorVisit> query, VisitorSearchRequest request)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!string.IsNullOrWhiteSpace(request.QuickFilter))
        {
            switch (request.QuickFilter.ToLowerInvariant())
            {
                case "today":
                    query = query.Where(v => v.VisitDate == today); break;
                case "yesterday":
                    query = query.Where(v => v.VisitDate == today.AddDays(-1)); break;
                case "thisweek":
                    var start = today.AddDays(-(int)DateTime.Now.DayOfWeek);
                    query = query.Where(v => v.VisitDate >= start && v.VisitDate <= today); break;
                case "thismonth":
                    var monthStart = new DateOnly(today.Year, today.Month, 1);
                    query = query.Where(v => v.VisitDate >= monthStart && v.VisitDate <= today); break;
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
            query = query.Where(v => v.VisitNumber.Contains(request.VisitorNumber) || v.Visitor.VisitorNumber.Contains(request.VisitorNumber));
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
            VisitorNumber = visit.Visitor.VisitorNumber,
            VisitNumber = visit.VisitNumber,
            VisitorName = visit.Visitor.FullName,
            CompanyName = visit.Visitor.CompanyName,
            Phone = visit.Visitor.Phone,
            Email = visit.Visitor.Email,
            HostName = visit.HostEmployee.FullName,
            DepartmentName = visit.Department.Name,
            VisitDate = visit.VisitDate,
            VisitTime = visit.VisitTime,
            Status = visit.Status,
            StatusLabel = visit.Status.ToString(),
            Purposes = visit.VisitPurposes.Select(p => p.VisitPurpose.Name).ToList(),
            Locations = visit.VisitLocations.Select(l => l.Location.Name).ToList(),
            PhotoUrl = photo is null ? null : _media.ToPublicApiPath(Path.GetFileName(photo.FilePath)),
            CheckInAt = visit.CheckInAt,
            CheckOutAt = visit.CheckOutAt,
            DurationMinutes = duration,
            IsLongStay = longStay,
            PreRegistrationReference = visit.PreRegistrationReference,
            PassCode = visit.Passes.OrderByDescending(p => p.IssuedAt).FirstOrDefault()?.PassCode,
            NumberOfPersons = visit.NumberOfPersons < 1 ? 1 : visit.NumberOfPersons
        };
    }

    private async Task<PassDto> BuildPassDto(VisitorVisit visit, VisitorPass pass, string webRoot)
    {
        var settings = await _settings.GetAsync();
        var item = MapListItem(visit, settings.MaxVisitDurationWarningMinutes);
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
            Locations = item.Locations,
            VisitNumber = item.VisitNumber,
            CheckInAt = item.CheckInAt,
            PhotoUrl = item.PhotoUrl,
            Status = item.StatusLabel
        };
    }

    /// <summary>
    /// Finds the closest stored face template within the caller's tenant.
    /// Templates are streamed rather than materialised so large tenants stay bounded in memory.
    /// </summary>
    public async Task<FaceSearchMatchDto?> FaceSearchAsync(float[] descriptor, ClaimsPrincipal user)
    {
        _ = ResolveTenantId(user);

        if (descriptor is null || descriptor.Length != FaceRecognition.Dimensions)
            throw new InvalidOperationException("A valid face capture is required.");

        Guid? bestVisitorId = null;
        var bestDistance = double.MaxValue;

        var candidates = _db.VisitorFaceDescriptors.AsNoTracking()
            .Where(f => f.Model == FaceRecognition.ModelId && f.Dimensions == FaceRecognition.Dimensions)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new { f.VisitorId, f.Descriptor })
            .AsAsyncEnumerable();

        await foreach (var candidate in candidates)
        {
            var stored = FromBytes(candidate.Descriptor);
            if (stored.Length != descriptor.Length) continue;

            var distance = EuclideanDistance(descriptor, stored);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestVisitorId = candidate.VisitorId;
            }
        }

        if (bestVisitorId is null || bestDistance > FaceRecognition.RegistrationMatchThreshold)
        {
            await _audit.LogAsync("FaceSearchNoMatch", "Visitor", null, "Face capture did not match a known visitor");
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
                v.Phone,
                v.Email,
                TotalVisits = v.Visits.Count,
                LastVisitDate = v.Visits.OrderByDescending(x => x.VisitDate).Select(x => (DateOnly?)x.VisitDate).FirstOrDefault(),
                PhotoPath = v.Photos.Where(p => p.IsPrimary).Select(p => p.FilePath).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        // Tenant query filter removed the visitor: treat as no match rather than leaking existence.
        if (visitor is null) return null;

        await _audit.LogAsync("FaceSearchMatched", "Visitor", visitor.Id.ToString(), "Returning visitor recognised from face capture");

        return new FaceSearchMatchDto
        {
            VisitorId = visitor.Id,
            VisitorNumber = visitor.VisitorNumber,
            VisitorName = visitor.FullName,
            CompanyName = visitor.CompanyName,
            Phone = visitor.Phone,
            Email = visitor.Email,
            PhotoUrl = visitor.PhotoPath is null ? null : _media.ToPublicApiPath(Path.GetFileName(visitor.PhotoPath)),
            LastVisitDate = visitor.LastVisitDate,
            TotalVisits = visitor.TotalVisits,
            Distance = Math.Round(bestDistance, 4)
        };
    }

    private async Task SaveFaceDescriptorAsync(Guid visitorId, float[] descriptor, ClaimsPrincipal user)
    {
        if (descriptor.Length != FaceRecognition.Dimensions) return;

        var existing = await _db.VisitorFaceDescriptors
            .Where(f => f.VisitorId == visitorId && f.Model == FaceRecognition.ModelId)
            .ToListAsync();
        _db.VisitorFaceDescriptors.RemoveRange(existing);

        _db.VisitorFaceDescriptors.Add(new VisitorFaceDescriptor
        {
            TenantId = ResolveTenantId(user),
            VisitorId = visitorId,
            Descriptor = ToBytes(descriptor),
            Dimensions = descriptor.Length,
            Model = FaceRecognition.ModelId,
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

    private static double EuclideanDistance(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return Math.Sqrt(sum);
    }

    private async Task SavePhotoAsync(Guid visitorId, Guid visitId, string base64, string webRoot, string? userName)
    {
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

    private async Task<string> NextVisitorNumberAsync(string prefix)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var patternPrefix = $"{prefix}-V-{date}-";
        var latest = await _db.Visitors.AsNoTracking()
            .Where(v => v.VisitorNumber.StartsWith(patternPrefix))
            .OrderByDescending(v => v.VisitorNumber)
            .Select(v => v.VisitorNumber)
            .FirstOrDefaultAsync();
        var next = latest is null ? 1 : MaxNumericSuffix([latest], patternPrefix) + 1;
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
        var year = DateTime.Now.ToString("yyyy");
        var code = string.IsNullOrWhiteSpace(prefix) ? "VMS" : prefix.Trim().ToUpperInvariant();
        return $"{code}-{year}-";
    }

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
    private async Task SaveNewVisitAsync(VisitorVisit visit, Visitor? newVisitor, string prefix)
    {
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

                    visit.VisitNumber = await NextVisitNumberAsync(prefix);
                    if (newVisitor is not null)
                        newVisitor.VisitorNumber = await NextVisitorNumberAsync(prefix);

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
    /// Serialises number allocation for one tenant and year. The lock is owned by the surrounding
    /// transaction, so it is always released on commit or rollback. Providers without application
    /// locks fall back to the unique index plus retry.
    /// </summary>
    private async Task AcquireNumberAllocationLockAsync(Guid tenantId, string prefix)
    {
        if (!_db.Database.IsSqlServer()) return;

        var resource = $"vms-number:{tenantId}:{VisitNumberPrefix(prefix)}";
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
            resource);
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
        if (request.DepartmentId == Guid.Empty)
            throw new InvalidOperationException("Department is required.");
        if (!request.HostEmployeeId.HasValue && string.IsNullOrWhiteSpace(request.HostName))
            throw new InvalidOperationException("Host name is required.");
        if (request.PurposeIds.Count == 0 && string.IsNullOrWhiteSpace(request.PurposeNotes))
            throw new InvalidOperationException("Purpose of visit is required.");
        if (!string.IsNullOrWhiteSpace(request.Telephone))
        {
            var phone = Regex.Replace(request.Telephone.Trim(), @"[\s\-()]", "");
            if (!IndianPhone.IsMatch(phone) && !IndianPhone.IsMatch(request.Telephone.Trim()))
                throw new InvalidOperationException("Telephone number format is invalid. Use a 10-digit Indian mobile (e.g. 9876543210).");
            request.Telephone = phone;
        }

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
            if (byId.DepartmentId != departmentId)
                throw new InvalidOperationException("Host does not belong to the selected department.");
            return byId;
        }

        var name = hostName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Host name is required.");

        var existing = await _db.Employees.Include(e => e.Department)
            .FirstOrDefaultAsync(e =>
                e.DepartmentId == departmentId &&
                e.IsActive &&
                e.FullName.ToLower() == name.ToLower());

        if (existing is not null)
            return existing;

        var created = new Employee
        {
            TenantId = TenantClaims.RequireTenantId(null, _tenant),
            FullName = name,
            DepartmentId = departmentId,
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
            throw new UnauthorizedAccessException("You can only approve visitors assigned to you.");
    }
}
