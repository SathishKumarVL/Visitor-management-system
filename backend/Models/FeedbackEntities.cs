using System.ComponentModel.DataAnnotations;

namespace Tiaano.Vms.Api.Models;

/// <summary>Tenant-configurable checkout feedback prompt rated 1–5 stars.</summary>
public class FeedbackQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(300)]
    public string Prompt { get; set; } = string.Empty;

    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>One feedback submission per visit, stored with visitor identity snapshot.</summary>
public class VisitFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid VisitorVisitId { get; set; }
    public VisitorVisit VisitorVisit { get; set; } = null!;

    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    [Required, MaxLength(150)]
    public string VisitorName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [Required, MaxLength(50)]
    public string VisitNumber { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Comments { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string? SubmittedBy { get; set; }

    public ICollection<VisitFeedbackAnswer> Answers { get; set; } = new List<VisitFeedbackAnswer>();
}

public class VisitFeedbackAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VisitFeedbackId { get; set; }
    public VisitFeedback VisitFeedback { get; set; } = null!;

    public Guid? FeedbackQuestionId { get; set; }
    public FeedbackQuestion? FeedbackQuestion { get; set; }

    [Required, MaxLength(300)]
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>Star rating from 1 (lowest) to 5 (highest).</summary>
    public int Rating { get; set; }
}
