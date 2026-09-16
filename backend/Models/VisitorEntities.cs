using System.ComponentModel.DataAnnotations;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Models;

public class Visitor
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(50)]
    public string VisitorNumber { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<VisitorVisit> Visits { get; set; } = new List<VisitorVisit>();
    public ICollection<VisitorPhoto> Photos { get; set; } = new List<VisitorPhoto>();
    public ICollection<VisitorDocument> Documents { get; set; } = new List<VisitorDocument>();
}

public class VisitorVisit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    [Required, MaxLength(50)]
    public string VisitNumber { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PreRegistrationReference { get; set; }

    public VisitorType VisitorType { get; set; } = VisitorType.WalkIn;
    public VisitStatus Status { get; set; } = VisitStatus.PendingApproval;

    public DateOnly VisitDate { get; set; }
    public TimeOnly VisitTime { get; set; }

    public DateOnly? ExpectedDate { get; set; }
    public TimeOnly? ExpectedTime { get; set; }

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid HostEmployeeId { get; set; }
    public Employee HostEmployee { get; set; } = null!;

    [MaxLength(50)]
    public string? HostIntercom { get; set; }

    [MaxLength(1000)]
    public string? PurposeNotes { get; set; }

    [MaxLength(100)]
    public string? PlantNumber { get; set; }

    [MaxLength(200)]
    public string? OtherLocationText { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>How many people are included in this visit (including the primary visitor).</summary>
    public int NumberOfPersons { get; set; } = 1;

    public bool IsWalkIn { get; set; }

    public DateTime? CheckInAt { get; set; }
    public Guid? EntryGateId { get; set; }
    public EntryGate? EntryGate { get; set; }
    public string? CheckedInByUserId { get; set; }
    public ApplicationUser? CheckedInByUser { get; set; }
    public string? SecurityCheckInUserId { get; set; }
    public ApplicationUser? SecurityCheckInUser { get; set; }

    public DateTime? CheckOutAt { get; set; }
    public Guid? ExitGateId { get; set; }
    public ExitGate? ExitGate { get; set; }
    public string? CheckedOutByUserId { get; set; }
    public ApplicationUser? CheckedOutByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<VisitorVisitPurpose> VisitPurposes { get; set; } = new List<VisitorVisitPurpose>();
    public ICollection<VisitorVisitLocation> VisitLocations { get; set; } = new List<VisitorVisitLocation>();
    public ICollection<Approval> Approvals { get; set; } = new List<Approval>();
    public ICollection<VisitorPass> Passes { get; set; } = new List<VisitorPass>();
}

public class VisitorVisitPurpose
{
    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    public Guid VisitPurposeId { get; set; }
    public VisitPurpose VisitPurpose { get; set; } = null!;
}

public class VisitorVisitLocation
{
    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;
}

public class VisitorPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public Guid? VisitorVisitId { get; set; }
    public VisitorVisit? VisitorVisit { get; set; }

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = "image/jpeg";

    public long FileSizeBytes { get; set; }
    public bool IsPrimary { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Biometric face template (not an image) used to recognise returning visitors.
/// Tenant-scoped explicitly because this is sensitive personal data.
/// </summary>
public class VisitorFaceDescriptor
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    /// <summary>
    /// Little-endian float32 vector: 512 dimensions for ArcFace, 128 for legacy face-api templates.
    /// Sized beyond ArcFace's 2048 bytes so a future model does not require a schema change, but kept
    /// under SQL Server's 8000-byte limit so the column stays in-row rather than becoming a BLOB.
    /// </summary>
    [Required, MaxLength(4096)]
    public byte[] Descriptor { get; set; } = Array.Empty<byte>();

    public int Dimensions { get; set; }

    /// <summary>Model identifier so templates from different models are never compared.</summary>
    [Required, MaxLength(50)]
    public string Model { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

public static class FaceRecognition
{
    /// <summary>
    /// Templates produced in the browser by face-api.js before recognition moved server-side. Rows
    /// tagged with this model are never compared against ArcFace vectors — the two embedding spaces
    /// are unrelated — so historic data is simply ignored until the visitor re-enrols.
    /// </summary>
    public const string LegacyModelId = "faceapi-128";
    public const int LegacyDimensions = 128;

    /// <summary>
    /// Templates kept per visitor. Several captures across different lighting and poses recognise a
    /// returning visitor far more reliably than the single most recent one.
    /// </summary>
    public const int MaxTemplatesPerVisitor = 5;
}

public class VisitorDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public Guid? VisitorVisitId { get; set; }
    public VisitorVisit? VisitorVisit { get; set; }

    public Guid IdTypeId { get; set; }
    public IdType IdType { get; set; } = null!;

    [Required, MaxLength(500)]
    public string IdNumberEncrypted { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string IdNumberMasked { get; set; } = string.Empty;

    public IdVerificationStatus VerificationStatus { get; set; } = IdVerificationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

public class Approval
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public string? RequestedToUserId { get; set; }
    public ApplicationUser? RequestedToUser { get; set; }

    public Guid? HostEmployeeId { get; set; }
    public Employee? HostEmployee { get; set; }

    public string? ActionByUserId { get; set; }
    public ApplicationUser? ActionByUser { get; set; }

    public DateTime? ActionAt { get; set; }

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VisitorPass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    [Required, MaxLength(80)]
    public string PassCode { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public int PrintCount { get; set; }

    public string? IssuedByUserId { get; set; }
    public ApplicationUser? IssuedByUser { get; set; }

    public DateTime? LastPrintedAt { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? TenantId { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Entity { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? EntityId { get; set; }

    public string? UserId { get; set; }
    public string? UserName { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Append-only roll-call log for an evacuation. Emergency marshalling is an operational event,
/// not a property of the visit, so nothing here rewrites the visit's own history.
/// </summary>
public class EmergencyRollCallEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    public EmergencyRollCallStatus Status { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? RecordedByUserId { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }
}

public class NotificationOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning tenant, stamped at queue time. A delivery worker must resolve sender configuration
    /// from this value rather than from whatever tenant happens to be ambient when it runs.
    /// Nullable only so pre-existing rows survive the migration; new rows always carry a tenant.
    /// </summary>
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(50)]
    public string Channel { get; set; } = "Internal";

    [Required, MaxLength(200)]
    public string Recipient { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
}
