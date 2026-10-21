using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Models;

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;

    /// <summary>
    /// JSON array of menu keys the user may see (e.g. ["dashboard","visitors"]).
    /// Null means use the full set allowed by their role.
    /// </summary>
    [MaxLength(1000)]
    public string? AllowedMenuKeysJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<Employee> EmployeeProfiles { get; set; } = new List<Employee>();
}

/// <summary>
/// A face that may sign in as a user. Several people (e.g. guards on different shifts) can share one
/// login, so each face carries the name of the person it belongs to.
/// </summary>
public class UserFaceLogin
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(100)]
    public string PersonName { get; set; } = string.Empty;

    /// <summary>Little-endian float32 vector, same format as visitor face templates.</summary>
    [Required, MaxLength(4096)]
    public byte[] Descriptor { get; set; } = Array.Empty<byte>();

    public int Dimensions { get; set; }

    [Required, MaxLength(50)]
    public string Model { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    [MaxLength(50)]
    public string? Intercom { get; set; }

    /// <summary>When set, department is only offered for this location. Null = all locations.</summary>
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Name prefix chosen in the Employee menu: one of <see cref="EmployeeTitles.All"/>.</summary>
    [MaxLength(10)]
    public string? Title { get; set; }

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(50)]
    public string? Intercom { get; set; }

    [MaxLength(100)]
    public string? Designation { get; set; }

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public EmployeeGender? Gender { get; set; }

    /// <summary>Stored file name of the enrolment photo in the tenant's private media folder.</summary>
    [MaxLength(260)]
    public string? PhotoPath { get; set; }

    /// <summary>Little-endian float32 vector, same format as login face templates.</summary>
    [MaxLength(4096)]
    public byte[]? FaceDescriptor { get; set; }

    public int? FaceDimensions { get; set; }

    [MaxLength(50)]
    public string? FaceModel { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Job titles offered in the Employees form. Employees keep the title as text, so deleting or deactivating a
/// designation never changes existing employee records; renaming one renames it on its employees.
/// </summary>
public class Designation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public class VisitPurpose
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    /// <summary>When set, purpose is only offered for this location. Null = all locations.</summary>
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>JSON array of <see cref="PurposeField"/>: extra text fields asked at registration for this purpose.</summary>
    public string? FieldsJson { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>One extra text field of a purpose, stored in <see cref="VisitPurpose.FieldsJson"/>.</summary>
public class PurposeField
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    /// <summary><see cref="PurposeFieldTypes"/>; missing in fields saved before lookups existed, which are text.</summary>
    public string? Type { get; set; }
    public Guid? LookupSourceId { get; set; }
}

public static class PurposeFieldTypes
{
    public const string Text = "text";
    public const string Lookup = "lookup";
}

public class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    public bool RequiresPlantNumber { get; set; }
    public bool RequiresOtherText { get; set; }
    /// <summary>Off: visits here are marked "MoM not required", so no minutes reminders and nothing pending.</summary>
    public bool MomRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// A room at a location. Allocated automatically at check-in to the smallest free hall that seats the party
/// (halls of 6+ seats are kept for groups of 6+); a hall is busy while a visit holding it is still inside.
/// </summary>
public class MeetingHall
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public int SeatingCapacity { get; set; }

    [MaxLength(50)]
    public string? IntercomNo { get; set; }

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public class IdType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
