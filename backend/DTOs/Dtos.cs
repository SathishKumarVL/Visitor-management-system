using System.ComponentModel.DataAnnotations;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.DTOs;

public record ApiResponse<T>(bool Success, T? Data, string? Message = null, IEnumerable<string>? Errors = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime RefreshExpiresAt { get; set; }
    public UserDto User { get; set; } = null!;

    /// <summary>Set only for face sign-in: the person whose registered face matched.</summary>
    public string? FacePersonName { get; set; }
}

public class FaceLoginRequest
{
    [Required]
    public string PhotoBase64 { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class FaceLoginStatusDto
{
    public bool Available { get; set; }
}

public class UserFaceLoginDto
{
    public Guid Id { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class AddUserFaceLoginRequest
{
    [Required(ErrorMessage = "Enter the name of the person whose face this is."), MaxLength(100)]
    public string PersonName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Capture the person's face.")]
    public string PhotoBase64 { get; set; } = string.Empty;
}

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Enter your username or email.")]
    public string UsernameOrEmail { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(10, ErrorMessage = "Password must be at least 10 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminResetPasswordRequest
{
    [Required, MinLength(10, ErrorMessage = "Password must be at least 10 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>Force the user to choose their own password at next sign-in.</summary>
    public bool MustChangePassword { get; set; } = true;
}

public class PublicBrandingDto
{
    public string CompanyName { get; set; } = "TIAANO";
    public string LogoPath { get; set; } = "/branding/tiaano-logo.png";
    public string ThemePreset { get; set; } = "tiaano";
    public string FontPreset { get; set; } = "inter";
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }

    /// <summary>Null means the user is not bound to a location and keeps tenant-wide visibility.</summary>
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }

    /// <summary>Menu keys visible to this user (already intersected with role). Empty uses role defaults on the client.</summary>
    public IReadOnlyList<string> AllowedMenuKeys { get; set; } = Array.Empty<string>();

    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Number of faces registered for face sign-in (only filled on the admin user list).</summary>
    public int FaceLoginCount { get; set; }
}

public class CreateUserRequest
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    public Guid? DepartmentId { get; set; }

    /// <summary>Leave null to grant tenant-wide visibility; set it to lock the user to one location.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Sidebar menus this user may see. Restricted to menus allowed for <see cref="Role"/>.</summary>
    public List<string>? AllowedMenuKeys { get; set; }

    [Required, MinLength(10)]
    public string Password { get; set; } = string.Empty;
}

public class UpdateUserRequest
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    public Guid? DepartmentId { get; set; }

    /// <summary>Leave null to grant tenant-wide visibility; set it to lock the user to one location.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Sidebar menus this user may see. Restricted to menus allowed for <see cref="Role"/>.</summary>
    public List<string>? AllowedMenuKeys { get; set; }

    public bool IsActive { get; set; } = true;
}

public class MasterItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public string? Code { get; set; }
    public string? Intercom { get; set; }
    public bool RequiresPlantNumber { get; set; }
    public bool RequiresOtherText { get; set; }
    public bool IsDefault { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Intercom { get; set; }
    public string? Designation { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? UserId { get; set; }
    /// <summary>Login username of the linked Host account, if any.</summary>
    public string? UserName { get; set; }
    public bool IsActive { get; set; }
}

public class CreateEmployeeLoginRequest
{
    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Defaults to the employee's email when left blank.</summary>
    [EmailAddress]
    public string? Email { get; set; }

    [Required, MinLength(10)]
    public string Password { get; set; } = string.Empty;
}

public class CreateEmployeeRequest
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;
    [MaxLength(50)]
    public string? Code { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Intercom { get; set; }
    public string? Designation { get; set; }
    [Required]
    public Guid DepartmentId { get; set; }
    public Guid? LocationId { get; set; }
    public string? UserId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RegisterVisitorRequest
{
    [Required, MaxLength(150)]
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>Male → Mr., Female → Ms. in mail templates via {{Salutation}}.</summary>
    [Required]
    public VisitorGender Gender { get; set; }

    public DateOnly? VisitDate { get; set; }
    public TimeOnly? VisitTime { get; set; }

    [MaxLength(20)]
    public string? Telephone { get; set; }

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    public Guid DepartmentId { get; set; }

    public Guid? HostEmployeeId { get; set; }

    [MaxLength(150)]
    public string? HostName { get; set; }

    public string? Intercom { get; set; }

    [MinLength(1)]
    public List<Guid> PurposeIds { get; set; } = new();

    public List<Guid> LocationIds { get; set; } = new();

    public string? PlantNumber { get; set; }
    public string? OtherLocationText { get; set; }
    public string? PurposeNotes { get; set; }
    public string? Notes { get; set; }
    public bool IsWalkIn { get; set; } = true;
    public int NumberOfPersons { get; set; } = 1;
    public string? PhotoBase64 { get; set; }

    /// <summary>Static ID proof name: Aadhaar, Pan Card, or Passport.</summary>
    [MaxLength(100)]
    public string? IdTypeName { get; set; }

    public Guid? IdTypeId { get; set; }
    public string? IdNumber { get; set; }
    public Guid? ExpectedVisitId { get; set; }

    /// <summary>
    /// Visitor the operator confirmed from the face match. The visit is still new; this only keeps the
    /// return visit attached to the existing visitor instead of creating a duplicate record.
    /// </summary>
    public Guid? RecognizedVisitorId { get; set; }

    /// <summary>The desk said a matching booking was not this visitor; do not link one automatically.</summary>
    public bool SkipBookingMatch { get; set; }

    /// <summary>
    /// Optional client-supplied value (ignored). The server always allocates from the tenant series.
    /// </summary>
    [MaxLength(80)]
    public string? PassNumber { get; set; }
}

public class PassNumberSeriesDto
{
    public Guid Id { get; set; }
    public Guid? LocationId { get; set; }
    public string Prefix { get; set; } = "VMS-";
    public int? Year { get; set; }
    public int StartNumber { get; set; }
    public int EndNumber { get; set; }
    public int CurrentNumber { get; set; }
    public bool IsActive { get; set; }
    public string FormatExample { get; set; } = string.Empty;
}

public class UpsertPassNumberSeriesRequest
{
    public Guid? LocationId { get; set; }

    [Required, MaxLength(20)]
    public string Prefix { get; set; } = "VMS-";

    public int? Year { get; set; }
    public int StartNumber { get; set; } = 1000;
    public int EndNumber { get; set; } = 999999;
    public int? CurrentNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RegisterDevicePushTokenRequest
{
    [Required, MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Platform { get; set; } = "unknown";
}

public class FaceSearchRequest
{
    /// <summary>
    /// Base64 image, with or without a data URL prefix. The face template is derived on the server;
    /// clients never submit embeddings, so a caller cannot craft one that matches a chosen visitor.
    /// </summary>
    [Required]
    public string PhotoBase64 { get; set; } = string.Empty;
}

/// <summary>Result of the exactly-one-face visitor photo gate (no persistence).</summary>
public class FacePhotoValidationDto
{
    public int FaceCount { get; set; }
    public bool Accepted { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class FaceSearchMatchDto
{
    public Guid VisitorId { get; set; }
    public string VisitorNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public VisitorGender? Gender { get; set; }
    public string? PhotoUrl { get; set; }
    public DateOnly? LastVisitDate { get; set; }
    public int TotalVisits { get; set; }

    /// <summary>Cosine similarity — higher is a closer match. Surfaced for operator transparency.</summary>
    public double Similarity { get; set; }

    /// <summary>True when this visitor already has an open (Inside) visit and must check out first.</summary>
    public bool IsCurrentlyInside { get; set; }
}

/// <summary>A visitor currently inside, recognised from a live capture at the exit desk.</summary>
public class FaceCheckoutMatchDto
{
    public Guid VisitId { get; set; }
    public Guid VisitorId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string Salutation { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string VisitNumber { get; set; } = string.Empty;
    public Guid? LocationId { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime? CheckInAt { get; set; }
    public double Similarity { get; set; }
}

public class ExpectedVisitorRequest
{
    [Required, MaxLength(150)]
    public string VisitorName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    public VisitorGender Gender { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    [Required]
    public DateOnly ExpectedDate { get; set; }

    [Required]
    public TimeOnly ExpectedTime { get; set; }

    public Guid? HostEmployeeId { get; set; }

    [MaxLength(150)]
    public string? HostName { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }

    public List<Guid> PurposeIds { get; set; } = new();
    public List<Guid> LocationIds { get; set; } = new();
    public string? PlantNumber { get; set; }
    public string? OtherLocationText { get; set; }

    [MaxLength(500)]
    public string? PurposeNotes { get; set; }

    public string? Notes { get; set; }
}

public class VisitorListItemDto
{
    public Guid VisitId { get; set; }
    public Guid VisitorId { get; set; }
    public string VisitorNumber { get; set; } = string.Empty;
    public string VisitNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public VisitorGender? Gender { get; set; }
    /// <summary>Mr. / Ms. derived from <see cref="Gender"/>.</summary>
    public string Salutation { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateOnly VisitDate { get; set; }
    public TimeOnly VisitTime { get; set; }
    public VisitStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public IReadOnlyList<string> Purposes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Locations { get; set; } = Array.Empty<string>();
    public string? PhotoUrl { get; set; }
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsLongStay { get; set; }
    public string? PreRegistrationReference { get; set; }
    public string? PassCode { get; set; }
    public int NumberOfPersons { get; set; } = 1;
    /// <summary>True when the host has saved meeting minutes for this visit.</summary>
    public bool HasMeetingMinutes { get; set; }
    /// <summary>Minutes of meeting waived; the check-out email does not wait for them.</summary>
    public bool MomNotRequired { get; set; }
    public string? MomNotRequiredBy { get; set; }
    public DateTime? MomNotRequiredAt { get; set; }
    /// <summary>Checked out from the list by an admin or by the system, not by the visitor.</summary>
    public bool IsManualCheckOut { get; set; }
}

public class VisitorDetailDto : VisitorListItemDto
{
    public string? Intercom { get; set; }
    public string? PurposeNotes { get; set; }
    public string? Notes { get; set; }
    public string? MeetingMinutes { get; set; }
    public string? PlantNumber { get; set; }
    public string? OtherLocationText { get; set; }
    public string? IdTypeName { get; set; }
    public string? IdNumberMasked { get; set; }
    public string? IdNumberFull { get; set; }
    public IdVerificationStatus IdVerificationStatus { get; set; }
    public string? CheckedInBy { get; set; }
    public string? CheckedOutBy { get; set; }
    public IReadOnlyList<ApprovalHistoryDto> ApprovalHistory { get; set; } = Array.Empty<ApprovalHistoryDto>();
    public VisitFeedbackDto? Feedback { get; set; }
}

/// <summary>An existing visitor found by mobile number, with their bookings that are still open.</summary>
public class VisitorPhoneLookupDto
{
    public Guid VisitorId { get; set; }
    public string VisitorNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public VisitorGender? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? LastVisitDate { get; set; }
    public int TotalVisits { get; set; }
    public IReadOnlyList<OpenBookingDto> OpenBookings { get; set; } = Array.Empty<OpenBookingDto>();
}

public class OpenBookingDto
{
    public Guid VisitId { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly? Time { get; set; }
    public Guid HostEmployeeId { get; set; }
    public string HostName { get; set; } = string.Empty;
}

/// <summary>A booking for today that the desk can link a walk-up registration to.</summary>
public class BookingMatchDto
{
    public Guid VisitId { get; set; }
    public Guid VisitorId { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public VisitorGender? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid HostEmployeeId { get; set; }
    public string HostName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public TimeOnly? ExpectedTime { get; set; }
    public IReadOnlyList<Guid> PurposeIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<string> Purposes { get; set; } = Array.Empty<string>();
    public string? PurposeNotes { get; set; }
    public int NumberOfPersons { get; set; } = 1;
}

public class ApprovalHistoryDto
{
    public Guid Id { get; set; }
    public ApprovalStatus Status { get; set; }
    public string? ActionBy { get; set; }
    public DateTime? ActionAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApprovalActionRequest
{
    [MaxLength(1000)]
    public string? Reason { get; set; }
}

public class CheckInRequest
{
    /// <summary>Browser/device instant (ISO-8601). When set, used for CheckInAt instead of server clock.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>Required when the visitor has no enrolled face photo yet (e.g. expected visitor arrival).</summary>
    public string? PhotoBase64 { get; set; }
}

public class CheckOutRequest
{
    /// <summary>Browser/device instant (ISO-8601). When set, used for CheckOutAt instead of server clock.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>When true (manual list check-out), feedback is not required — only CheckOutAt is recorded.</summary>
    public bool SkipFeedback { get; set; }
}

public class SaveMeetingMinutesRequest
{
    [Required, MaxLength(4000)]
    public string MeetingMinutes { get; set; } = string.Empty;
}

public class SetMomNotRequiredRequest
{
    public bool NotRequired { get; set; }
}

public class VisitorSearchRequest
{
    public string? Query { get; set; }
    public string? VisitorName { get; set; }
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public string? VisitorNumber { get; set; }
    public string? Host { get; set; }
    public Guid? DepartmentId { get; set; }
    public VisitStatus? Status { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? QuickFilter { get; set; }
    /// <summary>visitor, company, host, date, status, checkout; anything else sorts by registration time.</summary>
    public string? SortBy { get; set; }
    /// <summary>asc or desc (default).</summary>
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class DashboardDto
{
    public int VisitorsToday { get; set; }
    public int CurrentlyInside { get; set; }
    public int ExpectedToday { get; set; }
    public int PendingApprovals { get; set; }
    public int CheckedOutToday { get; set; }
    /// <summary>Check-outs today that used SuperAdmin manual check-out.</summary>
    public int ManualCheckOutsToday { get; set; }
    public IReadOnlyList<ChartPointDto> ByDepartment { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> ByPurpose { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> ByLocation { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> DailyTrend { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<VisitorListItemDto> RecentVisitors { get; set; } = Array.Empty<VisitorListItemDto>();
    public IReadOnlyList<VisitorListItemDto> PendingApprovalItems { get; set; } = Array.Empty<VisitorListItemDto>();
    public IReadOnlyList<VisitorListItemDto> CurrentlyInsideItems { get; set; } = Array.Empty<VisitorListItemDto>();
    /// <summary>True when the numbers are limited to the signed-in host's own visitors.</summary>
    public bool IsHostView { get; set; }
    /// <summary>Checked-out visits still waiting for minutes of meeting (check-out email is held until then).</summary>
    public int MomPendingCount { get; set; }
    public IReadOnlyList<VisitorListItemDto> MomPendingItems { get; set; } = Array.Empty<VisitorListItemDto>();
    /// <summary>SuperAdmin only: checked-out visits with no minutes, whether pending or marked not required.</summary>
    public int CheckedOutWithoutMomCount { get; set; }
    public IReadOnlyList<VisitorListItemDto> CheckedOutWithoutMomItems { get; set; } = Array.Empty<VisitorListItemDto>();
}

public class ChartPointDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class SettingsDto
{
    public string CompanyName { get; set; } = "TIAANO";
    public string LogoPath { get; set; } = "/branding/tiaano-logo.png";
    public string ThemePreset { get; set; } = "tiaano";
    public string FontPreset { get; set; } = "inter";
    public string VisitorIdPrefix { get; set; } = "TIA";
    public int VisitorPassValidityHours { get; set; } = 12;
    public bool ApprovalRequired { get; set; } = false;
    public bool WalkInApprovalRequired { get; set; } = false;
    public bool PhotoRequired { get; set; }
    public bool IdVerificationRequired { get; set; }
    public int MaxVisitDurationWarningMinutes { get; set; } = 240;
    public int SessionTimeoutMinutes { get; set; } = 480;

    // SMTP — configured in Settings UI. Password is write-only (never returned).
    public bool SmtpEnabled { get; set; } = true;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpFromAddress { get; set; } = string.Empty;
    public string SmtpFromName { get; set; } = "Visitor Management";
    public bool SmtpIgnoreSslErrors { get; set; }
    /// <summary>Optional new password. Null/empty keeps the existing stored password.</summary>
    public string? SmtpPassword { get; set; }
    public bool SmtpPasswordConfigured { get; set; }
}

public class PassDto
{
    public Guid PassId { get; set; }
    public Guid VisitId { get; set; }
    public string PassCode { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public IReadOnlyList<string> Purposes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Locations { get; set; } = Array.Empty<string>();
    public string VisitNumber { get; set; } = string.Empty;
    public DateTime? CheckInAt { get; set; }
    public string? PhotoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BrandCompanyName { get; set; } = string.Empty;
    public string BrandLogoPath { get; set; } = string.Empty;
}

public class EmergencyRosterDto
{
    public int TotalInside { get; set; }
    public int VisitorsInside { get; set; }

    /// <summary>False while the system has no employee presence data; the UI must not imply otherwise.</summary>
    public bool EmployeeTrackingAvailable { get; set; }

    public int Verified { get; set; }
    public int Evacuated { get; set; }
    public int Missing { get; set; }
    public int Unaccounted { get; set; }
    public IReadOnlyList<EmergencyRosterItemDto> Items { get; set; } = [];
}

public class EmergencyRosterItemDto
{
    public Guid VisitId { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public IReadOnlyList<string> Locations { get; set; } = [];
    public string? PhotoUrl { get; set; }
    public DateTime? CheckInAt { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int NumberOfPersons { get; set; }
    public EmergencyRollCallStatus RollCallStatus { get; set; }
    public DateTime? RollCallAt { get; set; }
}

public class EmergencyRollCallRequest
{
    [Required]
    public Guid VisitId { get; set; }

    [Required]
    public EmergencyRollCallStatus Status { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }
}

public class EmergencyRollCallResultDto
{
    public Guid VisitId { get; set; }
    public EmergencyRollCallStatus RollCallStatus { get; set; }
    public DateTime RollCallAt { get; set; }
}

public class ReportRequest
{
    public string ReportType { get; set; } = "daily";
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? HostEmployeeId { get; set; }
    public string? Company { get; set; }
    public Guid? PurposeId { get; set; }
    public Guid? LocationId { get; set; }
    public VisitStatus? Status { get; set; }
    public string Format { get; set; } = "json";
    /// <summary>JSON only: when set, <c>rows</c> holds just this page; totals still cover every row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    /// <summary>Column key (visitor, company, host, date, status, checkin, checkout, …); applies to exports too.</summary>
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
}

public class MasterUpsertRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Intercom { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresPlantNumber { get; set; }
    public bool RequiresOtherText { get; set; }
    public bool IsDefault { get; set; }
    public Guid? LocationId { get; set; }
}

public class MasterImportRowError
{
    public int Row { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MasterImportResultDto
{
    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<MasterImportRowError> Errors { get; set; } = new();
}

public class IntegrationApiKeyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class CreatedIntegrationApiKeyDto
{
    public IntegrationApiKeyDto Key { get; set; } = new();
    /// <summary>The full key. Returned only once, at creation.</summary>
    public string PlainKey { get; set; } = string.Empty;
}

public class CreateIntegrationApiKeyRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
}

/// <summary>One employee sent by another application. Matched on <see cref="Code"/>.</summary>
public class IntegrationEmployeeInput
{
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;
    /// <summary>Department name or code; must already exist.</summary>
    [Required, MaxLength(200)]
    public string Department { get; set; } = string.Empty;
    /// <summary>Location name or code. Omit to leave unchanged (new employees: all locations).</summary>
    public string? Location { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Intercom { get; set; }
    /// <summary>Send false to deactivate an employee who has left. Omit to leave unchanged (new employees: active).</summary>
    public bool? Active { get; set; }
}

public class IntegrationEmployeeBatchRequest
{
    [Required]
    public List<IntegrationEmployeeInput> Employees { get; set; } = new();
}

public class IntegrationEmployeeError
{
    /// <summary>Zero-based position in the request's employees array.</summary>
    public int Index { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class IntegrationEmployeeBatchResult
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<IntegrationEmployeeError> Errors { get; set; } = new();
}

public class IntegrationEmployeeDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string Department { get; set; } = string.Empty;
    public string? DepartmentCode { get; set; }
    public string? Location { get; set; }
    public string? LocationCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Intercom { get; set; }
    public bool Active { get; set; }
}

public class IntegrationLookupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool Active { get; set; }
}

public class MailContentTemplateDto
{
    public Guid Id { get; set; }
    public MailContentKind Kind { get; set; }
    public string KindLabel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class MailContentUpsertRequest
{
    [Required]
    public MailContentKind Kind { get; set; }
    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;
    [Required]
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid? LocationId { get; set; }
}

public class FeedbackQuestionDto
{
    public Guid Id { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class FeedbackQuestionUpsertRequest
{
    [Required, MaxLength(300)]
    public string Prompt { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid? LocationId { get; set; }
}

public class FeedbackAnswerInput
{
    [Required]
    public Guid QuestionId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }
}

public class SubmitVisitFeedbackRequest
{
    [Required]
    public List<FeedbackAnswerInput> Answers { get; set; } = new();

    [MaxLength(1000)]
    public string? Comments { get; set; }
}

public class VisitFeedbackDto
{
    public Guid Id { get; set; }
    public Guid VisitId { get; set; }
    public Guid VisitorId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string VisitNumber { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateTime SubmittedAt { get; set; }
    public IReadOnlyList<VisitFeedbackAnswerDto> Answers { get; set; } = Array.Empty<VisitFeedbackAnswerDto>();
}

public class VisitFeedbackAnswerDto
{
    public Guid QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public int Rating { get; set; }
}
