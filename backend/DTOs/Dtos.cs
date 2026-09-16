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

public class PublicBrandingDto
{
    public string CompanyName { get; set; } = "TIAANO";
    public string LogoPath { get; set; } = "/branding/tiaano-logo.png";
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
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; }
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
}

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Intercom { get; set; }
    public string? Designation { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public bool IsActive { get; set; }
}

public class CreateEmployeeRequest
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Intercom { get; set; }
    public string? Designation { get; set; }
    [Required]
    public Guid DepartmentId { get; set; }
    public string? UserId { get; set; }
}

public class RegisterVisitorRequest
{
    [Required, MaxLength(150)]
    public string VisitorName { get; set; } = string.Empty;

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
    public Guid? IdTypeId { get; set; }
    public string? IdNumber { get; set; }
    public Guid? ExpectedVisitId { get; set; }

    /// <summary>Optional face template captured with the photo, used to recognise return visits.</summary>
    public float[]? FaceDescriptor { get; set; }
}

public class FaceSearchRequest
{
    [Required]
    public float[] Descriptor { get; set; } = Array.Empty<float>();
}

public class FaceSearchMatchDto
{
    public Guid VisitorId { get; set; }
    public string VisitorNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public DateOnly? LastVisitDate { get; set; }
    public int TotalVisits { get; set; }

    /// <summary>Euclidean distance — lower is a closer match. Surfaced for operator transparency.</summary>
    public double Distance { get; set; }
}

public class ExpectedVisitorRequest
{
    [Required, MaxLength(150)]
    public string VisitorName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Email { get; set; }

    [Required]
    public DateOnly ExpectedDate { get; set; }

    [Required]
    public TimeOnly ExpectedTime { get; set; }

    [Required]
    public Guid HostEmployeeId { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }

    public List<Guid> PurposeIds { get; set; } = new();
    public List<Guid> LocationIds { get; set; } = new();
    public string? PlantNumber { get; set; }
    public string? OtherLocationText { get; set; }
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
}

public class VisitorDetailDto : VisitorListItemDto
{
    public string? Intercom { get; set; }
    public string? PurposeNotes { get; set; }
    public string? Notes { get; set; }
    public string? PlantNumber { get; set; }
    public string? OtherLocationText { get; set; }
    public string? IdTypeName { get; set; }
    public string? IdNumberMasked { get; set; }
    public string? IdNumberFull { get; set; }
    public IdVerificationStatus IdVerificationStatus { get; set; }
    public string? EntryGate { get; set; }
    public string? ExitGate { get; set; }
    public string? CheckedInBy { get; set; }
    public string? CheckedOutBy { get; set; }
    public IReadOnlyList<ApprovalHistoryDto> ApprovalHistory { get; set; } = Array.Empty<ApprovalHistoryDto>();
    public IReadOnlyList<AuditLogDto> AuditHistory { get; set; } = Array.Empty<AuditLogDto>();
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

public class AuditLogDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApprovalActionRequest
{
    [MaxLength(1000)]
    public string? Reason { get; set; }
}

public class CheckInRequest
{
    public Guid? EntryGateId { get; set; }
}

public class CheckOutRequest
{
    public Guid? ExitGateId { get; set; }
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
    public IReadOnlyList<ChartPointDto> ByDepartment { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> ByPurpose { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> ByLocation { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<ChartPointDto> DailyTrend { get; set; } = Array.Empty<ChartPointDto>();
    public IReadOnlyList<VisitorListItemDto> RecentVisitors { get; set; } = Array.Empty<VisitorListItemDto>();
    public IReadOnlyList<VisitorListItemDto> PendingApprovalItems { get; set; } = Array.Empty<VisitorListItemDto>();
    public IReadOnlyList<VisitorListItemDto> CurrentlyInsideItems { get; set; } = Array.Empty<VisitorListItemDto>();
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
    public string VisitorIdPrefix { get; set; } = "TIA";
    public int VisitorPassValidityHours { get; set; } = 12;
    public bool ApprovalRequired { get; set; } = false;
    public bool WalkInApprovalRequired { get; set; } = false;
    public bool PhotoRequired { get; set; }
    public bool IdVerificationRequired { get; set; }
    public int MaxVisitDurationWarningMinutes { get; set; } = 240;
    public string DefaultEntryGate { get; set; } = "Main Gate";
    public string DefaultExitGate { get; set; } = "Main Gate";
    public int SessionTimeoutMinutes { get; set; } = 480;
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
}
