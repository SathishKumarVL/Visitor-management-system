namespace Tiaano.Vms.Api.Models.Enums;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Reception = "Reception";
    public const string Security = "Security";
    public const string Host = "Host";

    public static readonly string[] All =
    [
        SuperAdmin, Admin, Reception, Security, Host
    ];
}

public enum VisitStatus
{
    Expected = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Inside = 5,
    CheckedOut = 6
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum IdVerificationStatus
{
    NotProvided = 0,
    Pending = 1,
    Verified = 2,
    Failed = 3
}

/// <summary>Outcome recorded for one person during an emergency roll call.</summary>
public enum EmergencyRollCallStatus
{
    Unknown = 0,
    Verified = 1,
    Evacuated = 2,
    Missing = 3
}

public enum VisitorType
{
    WalkIn = 0,
    Expected = 1,
    PreRegistered = 2
}
