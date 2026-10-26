using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Single-use links that let a visitor fill in their own details and face photo, which books an appointment with
/// the inviting employee. The public half runs without a signed-in user, so it trusts nothing but the link token.
/// </summary>
public interface IVisitorInvitationService
{
    /// <returns>The invitation, and why its email has not gone out yet (null when it was delivered).</returns>
    Task<(VisitorInvitationDto Invitation, string? EmailProblem)> CreateAsync(
        CreateVisitorInvitationRequest request, ClaimsPrincipal user, string? requestOrigin);

    /// <summary>Sends a fresh link (the previous one stops working) valid for another full period.</summary>
    Task<(VisitorInvitationDto Invitation, string? EmailProblem)> ResendAsync(Guid id, ClaimsPrincipal user, string? requestOrigin);

    Task WithdrawAsync(Guid id, ClaimsPrincipal user);
    Task<IReadOnlyList<VisitorInvitationDto>> ListAsync(ClaimsPrincipal user);
    Task<PublicInvitationDto> GetPublicAsync(string token);
    Task<PublicInvitationResultDto> SubmitAsync(string token, PublicInvitationSubmitRequest request);
}

public class VisitorInvitationService : IVisitorInvitationService
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);
    private const int MaxDaysAhead = 90;
    private const int ListDays = 14;
    private const int MaxPhotoBase64Length = 7_500_000;
    private const string RemovedBody = "[Invitation link removed: the invitation has been used, replaced or withdrawn]";

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IVisitMailService _mail;
    private readonly IVisitorService _visitors;
    private readonly IMasterDataService _masters;
    private readonly IEntitlementService _entitlements;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<VisitorInvitationService> _logger;

    public VisitorInvitationService(
        ApplicationDbContext db,
        ITenantContext tenant,
        IVisitMailService mail,
        IVisitorService visitors,
        IMasterDataService masters,
        IEntitlementService entitlements,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<VisitorInvitationService> logger)
    {
        _db = db;
        _tenant = tenant;
        _mail = mail;
        _visitors = visitors;
        _masters = masters;
        _entitlements = entitlements;
        _config = config;
        _env = env;
        _logger = logger;
    }

    // ------------------------------------------------------------------ staff

    public async Task<(VisitorInvitationDto Invitation, string? EmailProblem)> CreateAsync(
        CreateVisitorInvitationRequest request, ClaimsPrincipal user, string? requestOrigin)
    {
        var email = NormalizeEmail(request.Email);
        var host = await ResolveHostAsync(request.HostEmployeeId, user);
        var baseUrl = PublicBaseUrl(requestOrigin);

        // Inviting the same person to the same employee again replaces the earlier link instead of piling up invites.
        var invitation = await _db.VisitorInvitations
            .FirstOrDefaultAsync(i => i.Email == email && i.HostEmployeeId == host.Id
                                      && i.Status == VisitorInvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow);
        if (invitation is null)
        {
            invitation = new VisitorInvitation
            {
                TenantId = TenantClaims.RequireTenantId(user, _tenant),
                Email = email,
                HostEmployeeId = host.Id,
                LocationId = host.LocationId ?? _tenant.LocationId,
                CreatedBy = DisplayName(user),
            };
            _db.VisitorInvitations.Add(invitation);
        }

        var problem = await IssueLinkAsync(invitation, host, baseUrl, user, removeOnRefusal: invitation.SendCount == 0);
        return (await ToDtoAsync(invitation.Id), problem);
    }

    public async Task<(VisitorInvitationDto Invitation, string? EmailProblem)> ResendAsync(Guid id, ClaimsPrincipal user, string? requestOrigin)
    {
        var invitation = await FindForStaffAsync(id, user);
        if (invitation.Status != VisitorInvitationStatus.Pending)
            throw new InvalidOperationException("Only an invitation that has not been used or withdrawn can be sent again.");
        var host = await _db.Employees.Include(e => e.Department).FirstOrDefaultAsync(e => e.Id == invitation.HostEmployeeId && e.IsActive)
            ?? throw new InvalidOperationException("The employee on this invitation is no longer active.");

        var problem = await IssueLinkAsync(invitation, host, PublicBaseUrl(requestOrigin), user, removeOnRefusal: false);
        return (await ToDtoAsync(invitation.Id), problem);
    }

    public async Task WithdrawAsync(Guid id, ClaimsPrincipal user)
    {
        var invitation = await FindForStaffAsync(id, user);
        if (invitation.Status != VisitorInvitationStatus.Pending)
            throw new InvalidOperationException("Only an invitation that has not been used can be withdrawn.");
        invitation.Status = VisitorInvitationStatus.Withdrawn;
        invitation.UpdatedAt = DateTime.UtcNow;
        invitation.UpdatedBy = DisplayName(user);
        await _db.SaveChangesAsync();
        await RemoveLinksFromOutboxAsync(invitation.Id);
    }

    public async Task<IReadOnlyList<VisitorInvitationDto>> ListAsync(ClaimsPrincipal user)
    {
        var since = DateTime.UtcNow.AddDays(-ListDays);
        var query = _db.VisitorInvitations.AsNoTracking()
            .Where(i => i.CreatedAt >= since || (i.Status == VisitorInvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow));
        if (IsHostOnly(user))
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            query = query.Where(i => i.HostEmployee!.UserId == userId);
        }
        else if (_tenant.LocationId is Guid scope)
        {
            query = query.Where(i => i.LocationId == null || i.LocationId == scope);
        }

        var ids = await query.OrderByDescending(i => i.CreatedAt).Take(100).Select(i => i.Id).ToListAsync();
        return await ToDtosAsync(ids);
    }

    // ------------------------------------------------------------------ public

    public async Task<PublicInvitationDto> GetPublicAsync(string token)
    {
        var invitation = await OpenAsync(token);
        var host = invitation.HostEmployee!;

        var purposes = await _masters.GetPurposesAsync(true, invitation.LocationId);
        return new PublicInvitationDto
        {
            Email = invitation.Email,
            HostName = host.FullName,
            DepartmentName = host.Department?.Name,
            LocationName = await LocationNameAsync(invitation.LocationId),
            OrganisationName = await OrganisationNameAsync(invitation.TenantId),
            ExpiresAt = invitation.ExpiresAt,
            Purposes = purposes.Select(p => new PublicPurposeDto { Id = p.Id, Name = p.Name }).ToList(),
        };
    }

    public async Task<PublicInvitationResultDto> SubmitAsync(string token, PublicInvitationSubmitRequest request)
    {
        var invitation = await OpenAsync(token);
        var host = invitation.HostEmployee!;

        if (request.ExpectedDate > AppTime.Today.AddDays(MaxDaysAhead))
            throw new InvalidOperationException($"Please choose a date within the next {MaxDaysAhead} days.");
        if (string.IsNullOrWhiteSpace(request.PhotoBase64))
            throw new InvalidOperationException("A face photo is required.");
        if (request.PhotoBase64.Length > MaxPhotoBase64Length)
            throw new InvalidOperationException("The photo is too large. Please take it again.");
        _visitors.ValidateVisitorPhoto(request.PhotoBase64);

        // The claim is the single-use guarantee: of two submits at once, only one moves the row out of Pending.
        var now = DateTime.UtcNow;
        var claimed = await _db.VisitorInvitations.IgnoreQueryFilters()
            .Where(i => i.Id == invitation.Id && i.Status == VisitorInvitationStatus.Pending && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, VisitorInvitationStatus.Completed)
                .SetProperty(i => i.CompletedAt, now));
        if (claimed == 0)
            throw new InvalidOperationException("This invitation has already been used or has expired.");

        var booking = new ExpectedVisitorRequest
        {
            VisitorName = request.VisitorName.Trim(),
            CompanyName = request.CompanyName.Trim(),
            Gender = request.Gender,
            Phone = request.Phone,
            Email = invitation.Email,
            ExpectedDate = request.ExpectedDate,
            ExpectedTime = request.ExpectedTime,
            HostEmployeeId = host.Id,
            DepartmentId = host.DepartmentId,
            PurposeIds = request.PurposeIds.Distinct().ToList(),
            PurposeNotes = request.PurposeNotes,
            LocationIds = invitation.LocationId is Guid loc ? [loc] : [],
            Notes = $"Details filled in by the visitor from an invitation sent by {invitation.CreatedBy ?? "staff"}.",
        };

        (Guid VisitId, string VisitNumber) created;
        try
        {
            created = await _visitors.CreateInvitedBookingAsync(booking, request.PhotoBase64, $"Invitation ({invitation.Email})");
        }
        catch
        {
            // Let the visitor correct the form and try again with the same link.
            await _db.VisitorInvitations.IgnoreQueryFilters()
                .Where(i => i.Id == invitation.Id && i.Status == VisitorInvitationStatus.Completed && i.VisitorVisitId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, VisitorInvitationStatus.Pending)
                    .SetProperty(i => i.CompletedAt, (DateTime?)null));
            throw;
        }

        await _db.VisitorInvitations.IgnoreQueryFilters()
            .Where(i => i.Id == invitation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.VisitorVisitId, created.VisitId));
        await RemoveLinksFromOutboxAsync(invitation.Id);

        return new PublicInvitationResultDto
        {
            VisitNumber = created.VisitNumber,
            HostName = host.FullName,
            LocationName = await LocationNameAsync(invitation.LocationId),
            VisitDate = request.ExpectedDate,
            VisitTime = request.ExpectedTime,
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Rotates the token, extends the expiry and emails the new link.</summary>
    private async Task<string?> IssueLinkAsync(
        VisitorInvitation invitation, Employee host, string baseUrl, ClaimsPrincipal user, bool removeOnRefusal)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        invitation.TokenHash = HashToken(token);
        invitation.ExpiresAt = DateTime.UtcNow.Add(LinkLifetime);
        invitation.SendCount++;
        invitation.LastSentAt = DateTime.UtcNow;
        if (invitation.SendCount > 1)
        {
            invitation.UpdatedAt = DateTime.UtcNow;
            invitation.UpdatedBy = DisplayName(user);
        }
        await _db.SaveChangesAsync();
        await RemoveLinksFromOutboxAsync(invitation.Id);

        var ctx = new VisitMailContext(
            VisitorName: "",
            Salutation: "",
            VisitorEmail: invitation.Email,
            Company: "",
            VisitingPerson: host.FullName,
            VisitingPersonEmail: host.Email,
            VisitorNumber: "",
            VisitNumber: "",
            Location: await LocationNameAsync(invitation.LocationId) ?? await OrganisationNameAsync(invitation.TenantId),
            LocationId: invitation.LocationId,
            Department: host.Department?.Name ?? "",
            Purpose: "",
            MeetingMinutes: "",
            MomentLocal: AppTime.ToLocal(invitation.ExpiresAt),
            InviteLink: $"{baseUrl}/invite#{token}");

        var key = OutboxKey(invitation.Id, invitation.SendCount);
        if (!await _mail.SendVisitorInvitationAsync(ctx, key))
        {
            const string reason = " has no email address, and this server only sends visitor emails from the employee's own address.";
            if (removeOnRefusal)
            {
                _db.VisitorInvitations.Remove(invitation);
                await _db.SaveChangesAsync();
                throw new InvalidOperationException($"The invitation was not sent: {host.FullName}{reason}");
            }
            return $"The invitation email could not be sent: {host.FullName}{reason}";
        }

        var row = await _db.NotificationOutbox.AsNoTracking()
            .Where(n => n.IdempotencyKey == key)
            .Select(n => new { n.IsSent, n.Error })
            .FirstOrDefaultAsync();
        if (row is null || row.IsSent) return null;
        return $"The invitation is saved, but the email has not gone out yet ({row.Error ?? "unknown error"}). It is retried automatically.";
    }

    private async Task<Employee> ResolveHostAsync(Guid? requestedHostId, ClaimsPrincipal user)
    {
        if (IsHostOnly(user))
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var own = await _db.Employees.Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive)
                ?? throw new InvalidOperationException("Your login is not linked to an employee record, so you cannot send invitations.");
            if (requestedHostId is Guid other && other != Guid.Empty && other != own.Id)
                throw new UnauthorizedAccessException("You can only invite visitors to meet you.");
            return own;
        }

        if (requestedHostId is not Guid hostId || hostId == Guid.Empty)
            throw new InvalidOperationException("Select the employee the visitor will meet.");
        var host = await _db.Employees.Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == hostId && e.IsActive)
            ?? throw new InvalidOperationException("Selected employee was not found.");
        if (_tenant.LocationId is Guid scope && host.LocationId is Guid hostLocation && hostLocation != scope)
            throw new InvalidOperationException("Selected employee belongs to another location.");
        return host;
    }

    private async Task<VisitorInvitation> FindForStaffAsync(Guid id, ClaimsPrincipal user)
    {
        var invitation = await _db.VisitorInvitations.Include(i => i.HostEmployee).FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("Invitation not found.");
        if (IsHostOnly(user) && invitation.HostEmployee?.UserId != user.FindFirstValue(ClaimTypes.NameIdentifier))
            throw new UnauthorizedAccessException("You can only manage your own invitations.");
        if (!IsHostOnly(user) && _tenant.LocationId is Guid scope && invitation.LocationId is Guid loc && loc != scope)
            throw new UnauthorizedAccessException("This invitation belongs to another location.");
        return invitation;
    }

    /// <summary>Finds a usable invitation for a link and switches the request into its tenant.</summary>
    private async Task<VisitorInvitation> OpenAsync(string token)
    {
        const string invalid = "This invitation link is not valid. Please check that you opened the full link from the email.";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            throw new InvalidOperationException(invalid);

        var hash = HashToken(token.Trim());
        var invitation = await _db.VisitorInvitations.IgnoreQueryFilters()
            .Include(i => i.HostEmployee).ThenInclude(e => e!.Department)
            .FirstOrDefaultAsync(i => i.TokenHash == hash)
            ?? throw new InvalidOperationException(invalid);

        var hostName = invitation.HostEmployee?.FullName ?? "the person who invited you";
        switch (invitation.Status)
        {
            case VisitorInvitationStatus.Completed:
                throw new InvalidOperationException(
                    "This invitation has already been used. Your appointment is booked; the details were sent to your email.");
            case VisitorInvitationStatus.Withdrawn:
                throw new InvalidOperationException($"This invitation has been withdrawn. Please contact {hostName}.");
        }
        if (invitation.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException($"This invitation link has expired. Please ask {hostName} to send a new one.");
        if (invitation.HostEmployee is not { IsActive: true })
            throw new InvalidOperationException("This invitation is no longer valid.");

        _tenant.Set(invitation.TenantId);
        try
        {
            await _entitlements.EnsureModuleEnabledAsync(ModuleKeys.VisitorManagement);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Invitation {Id} opened while the tenant cannot book visitors: {Reason}", invitation.Id, ex.Message);
            throw new InvalidOperationException("This invitation is no longer valid.");
        }
        return invitation;
    }

    private async Task<VisitorInvitationDto> ToDtoAsync(Guid id) => (await ToDtosAsync([id])).Single();

    private async Task<IReadOnlyList<VisitorInvitationDto>> ToDtosAsync(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return [];
        var rows = await _db.VisitorInvitations.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new
            {
                Invitation = i,
                HostName = i.HostEmployee != null ? i.HostEmployee.FullName : "",
            })
            .ToListAsync();

        var locationIds = rows.Select(r => r.Invitation.LocationId).OfType<Guid>().Distinct().ToList();
        var locations = await _db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);
        var visitIds = rows.Select(r => r.Invitation.VisitorVisitId).OfType<Guid>().ToList();
        var visits = await _db.VisitorVisits.AsNoTracking().Where(v => visitIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Visitor.FullName, Date = v.ExpectedDate ?? v.VisitDate, Time = v.ExpectedTime ?? v.VisitTime })
            .ToDictionaryAsync(v => v.Id);

        var keys = rows.Select(r => OutboxKey(r.Invitation.Id, r.Invitation.SendCount)).ToList();
        var failed = await _db.NotificationOutbox.AsNoTracking()
            .Where(n => n.IdempotencyKey != null && keys.Contains(n.IdempotencyKey) && !n.IsSent)
            .Select(n => new { n.IdempotencyKey, n.Error })
            .ToListAsync();
        var errors = failed.ToDictionary(f => f.IdempotencyKey!, f => f.Error ?? "Not sent yet");

        var now = DateTime.UtcNow;
        var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return rows
            .OrderBy(r => order[r.Invitation.Id])
            .Select(r =>
            {
                var i = r.Invitation;
                var visit = i.VisitorVisitId is Guid vid && visits.TryGetValue(vid, out var v) ? v : null;
                return new VisitorInvitationDto
                {
                    Id = i.Id,
                    Email = i.Email,
                    HostEmployeeId = i.HostEmployeeId,
                    HostName = r.HostName,
                    LocationName = i.LocationId is Guid loc && locations.TryGetValue(loc, out var name) ? name : null,
                    Status = i.Status == VisitorInvitationStatus.Pending && i.ExpiresAt <= now ? "Expired" : i.Status.ToString(),
                    CreatedAt = i.CreatedAt,
                    CreatedBy = i.CreatedBy,
                    ExpiresAt = i.ExpiresAt,
                    LastSentAt = i.LastSentAt,
                    SendCount = i.SendCount,
                    CompletedAt = i.CompletedAt,
                    VisitorVisitId = visit is null ? null : i.VisitorVisitId,
                    VisitorName = visit?.FullName,
                    VisitDate = visit?.Date,
                    VisitTime = visit?.Time,
                    EmailError = i.Status == VisitorInvitationStatus.Pending
                        && errors.TryGetValue(OutboxKey(i.Id, i.SendCount), out var error) ? error : null,
                };
            })
            .ToList();
    }

    /// <summary>The outbox keeps message bodies; a link that no longer works has no business staying there.</summary>
    private async Task RemoveLinksFromOutboxAsync(Guid invitationId)
    {
        var prefix = $"visitor-invite:{invitationId}:";
        var current = await _db.VisitorInvitations.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Id == invitationId)
            .Select(i => new { i.Status, i.SendCount })
            .FirstOrDefaultAsync();
        var keep = current?.Status == VisitorInvitationStatus.Pending ? OutboxKey(invitationId, current.SendCount) : null;
        await _db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.IdempotencyKey != null && n.IdempotencyKey.StartsWith(prefix) && n.IdempotencyKey != keep)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Body, RemovedBody)
                .SetProperty(n => n.IsSent, true)
                .SetProperty(n => n.Error, (string?)null));
    }

    private async Task<string?> LocationNameAsync(Guid? locationId) =>
        locationId is Guid id
            ? await _db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == id).Select(l => l.Name).FirstOrDefaultAsync()
            : null;

    private async Task<string> OrganisationNameAsync(Guid tenantId) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync() ?? "";

    private string PublicBaseUrl(string? requestOrigin)
    {
        var baseUrl = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) && _env.IsDevelopment())
            baseUrl = requestOrigin?.Trim().TrimEnd('/');
        // Never built from the request's Host header: anyone could point the emailed link at their own site.
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Invitation links cannot be built because App:PublicBaseUrl is not configured on the server.");
        return baseUrl;
    }

    private static string NormalizeEmail(string raw)
    {
        var email = (raw ?? "").Trim();
        if (email.Length == 0) throw new InvalidOperationException("Enter the visitor's email address.");
        try
        {
            var parsed = new MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase) || !email.Contains('.', StringComparison.Ordinal))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Enter a valid email address.");
        }
        return email.ToLowerInvariant();
    }

    private static bool IsHostOnly(ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.Host)
        && !user.IsInRole(AppRoles.SuperAdmin) && !user.IsInRole(AppRoles.Admin) && !user.IsInRole(AppRoles.Reception);

    private static string? DisplayName(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue("fullName") ?? user.Identity?.Name;
        return name is { Length: > 256 } ? name[..256] : name;
    }

    private static string OutboxKey(Guid invitationId, int sendCount) => $"visitor-invite:{invitationId}:{sendCount}";

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
