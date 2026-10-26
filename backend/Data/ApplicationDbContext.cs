using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Product;

namespace Tiaano.Vms.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext? _tenant;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenant = null)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<VisitPurpose> VisitPurposes => Set<VisitPurpose>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<IdType> IdTypes => Set<IdType>();
    public DbSet<FeedbackQuestion> FeedbackQuestions => Set<FeedbackQuestion>();
    public DbSet<VisitFeedback> VisitFeedbacks => Set<VisitFeedback>();
    public DbSet<VisitFeedbackAnswer> VisitFeedbackAnswers => Set<VisitFeedbackAnswer>();
    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<VisitorVisit> VisitorVisits => Set<VisitorVisit>();
    public DbSet<VisitorVisitPurpose> VisitorVisitPurposes => Set<VisitorVisitPurpose>();
    public DbSet<VisitorVisitLocation> VisitorVisitLocations => Set<VisitorVisitLocation>();
    public DbSet<VisitorPhoto> VisitorPhotos => Set<VisitorPhoto>();
    public DbSet<VisitorDocument> VisitorDocuments => Set<VisitorDocument>();
    public DbSet<VisitorFaceDescriptor> VisitorFaceDescriptors => Set<VisitorFaceDescriptor>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<VisitorPass> VisitorPasses => Set<VisitorPass>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<EmergencyRollCallEvent> EmergencyRollCallEvents => Set<EmergencyRollCallEvent>();
    public DbSet<PassNumberSeries> PassNumberSeries => Set<PassNumberSeries>();
    public DbSet<PassNumberAllocation> PassNumberAllocations => Set<PassNumberAllocation>();
    public DbSet<PassNumberHold> PassNumberHolds => Set<PassNumberHold>();
    public DbSet<DevicePushToken> DevicePushTokens => Set<DevicePushToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProductModule> ProductModules => Set<ProductModule>();
    public DbSet<TenantModuleEntitlement> TenantModuleEntitlements => Set<TenantModuleEntitlement>();
    public DbSet<TenantLicense> TenantLicenses => Set<TenantLicense>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<ApplicationRelease> ApplicationReleases => Set<ApplicationRelease>();
    public DbSet<MailContentTemplate> MailContentTemplates => Set<MailContentTemplate>();
    public DbSet<IntegrationApiKey> IntegrationApiKeys => Set<IntegrationApiKey>();
    public DbSet<UserFaceLogin> UserFaceLogins => Set<UserFaceLogin>();
    public DbSet<MeetingHall> MeetingHalls => Set<MeetingHall>();
    public DbSet<LookupSource> LookupSources => Set<LookupSource>();
    public DbSet<VisitorInvitation> VisitorInvitations => Set<VisitorInvitation>();

    /// <summary>One inside visit per hall: concurrent check-ins cannot both take the same hall.</summary>
    public const string MeetingHallInsideIndex = "IX_VisitorVisits_MeetingHallId_Inside";

    /// <summary>A registration form submitted twice (retry after a timeout) can only create one visit.</summary>
    public const string ClientRequestIndex = "IX_VisitorVisits_TenantId_ClientRequestId";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserFaceLogin>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Model);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<IntegrationApiKey>(e =>
        {
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<LookupSource>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired();
        });

        builder.Entity<Department>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.Property(x => x.Name).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            // Fail closed: when a scoped tenant context exists without TenantId, return no rows (never all tenants).
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Designation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<MeetingHall>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LocationId });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.FullName);
            e.HasOne(x => x.Department).WithMany(d => d.Employees).HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany(u => u.EmployeeProfiles).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasOne(x => x.Department).WithMany(d => d.Users).HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.AllowedMenuKeysJson).HasMaxLength(1000);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Visitor>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.VisitorNumber }).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.CompanyName);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<VisitorVisit>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.VisitNumber }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.EntryNumber });
            e.Property(x => x.EntryNumber).HasMaxLength(50);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.Status, x.CheckOutAt });
            e.HasIndex(x => x.VisitDate);
            e.HasIndex(x => x.PreRegistrationReference);
            // Additive column: existing rows default to false (face/normal check-out).
            e.Property(x => x.IsManualCheckOut).HasDefaultValue(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Visitor).WithMany(v => v.Visits).HasForeignKey(x => x.VisitorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.HostEmployee).WithMany().HasForeignKey(x => x.HostEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MeetingHall).WithMany().HasForeignKey(x => x.MeetingHallId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MeetingHallId).IsUnique()
                .HasFilter($"[MeetingHallId] IS NOT NULL AND [Status] = {(int)Models.Enums.VisitStatus.Inside}")
                .HasDatabaseName(MeetingHallInsideIndex);
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique()
                .HasFilter("[ClientRequestId] IS NOT NULL")
                .HasDatabaseName(ClientRequestIndex);
            e.HasOne(x => x.CheckedInByUser).WithMany().HasForeignKey(x => x.CheckedInByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.SecurityCheckInUser).WithMany().HasForeignKey(x => x.SecurityCheckInUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CheckedOutByUser).WithMany().HasForeignKey(x => x.CheckedOutByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Location scope is additive to tenant scope: a user bound to a location only ever sees
            // visits that include that location; a user with no location keeps tenant-wide visibility.
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId
                && (_tenant.LocationId == null
                    || x.VisitLocations.Any(vl => vl.LocationId == _tenant.LocationId))));
        });

        builder.Entity<VisitorVisitPurpose>(e =>
        {
            e.HasKey(x => new { x.VisitorVisitId, x.VisitPurposeId });
            e.HasOne(x => x.VisitorVisit).WithMany(v => v.VisitPurposes).HasForeignKey(x => x.VisitorVisitId);
            e.HasOne(x => x.VisitPurpose).WithMany().HasForeignKey(x => x.VisitPurposeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VisitorVisitLocation>(e =>
        {
            e.HasKey(x => new { x.VisitorVisitId, x.LocationId });
            e.HasOne(x => x.VisitorVisit).WithMany(v => v.VisitLocations).HasForeignKey(x => x.VisitorVisitId);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VisitorPhoto>(e =>
        {
            e.HasOne(x => x.Visitor).WithMany(v => v.Photos).HasForeignKey(x => x.VisitorId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.VisitorVisit).WithMany().HasForeignKey(x => x.VisitorVisitId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<VisitorDocument>(e =>
        {
            e.HasOne(x => x.Visitor).WithMany(v => v.Documents).HasForeignKey(x => x.VisitorId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.IdType).WithMany().HasForeignKey(x => x.IdTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VisitorFaceDescriptor>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Model });
            e.HasOne(x => x.Visitor).WithMany().HasForeignKey(x => x.VisitorId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<EmergencyRollCallEvent>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.VisitorVisitId, x.RecordedAt });
            e.HasOne(x => x.VisitorVisit).WithMany().HasForeignKey(x => x.VisitorVisitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<NotificationOutbox>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IsSent });
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.SetNull);
            // Fail closed: a queued notification is only visible to the tenant that queued it.
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<PassNumberSeries>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IsActive });
            e.HasIndex(x => x.LocationId);
            e.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<PassNumberAllocation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.UserId, x.IsActive });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Series).WithMany(s => s.Allocations).HasForeignKey(x => x.SeriesId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<PassNumberHold>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.SeriesKey, x.Number }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<DevicePushToken>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.UserId });
            e.HasIndex(x => new { x.TenantId, x.Token }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Approval>(e =>
        {
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.VisitorVisit).WithMany(v => v.Approvals).HasForeignKey(x => x.VisitorVisitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedToUser).WithMany().HasForeignKey(x => x.RequestedToUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ActionByUser).WithMany().HasForeignKey(x => x.ActionByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.HostEmployee).WithMany().HasForeignKey(x => x.HostEmployeeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<VisitorPass>(e =>
        {
            // Physical badges are reused after check-out; only one active pass may hold a code.
            e.HasIndex(x => x.PassCode).IsUnique().HasFilter("[IsActive] = 1");
            e.HasOne(x => x.VisitorVisit).WithMany(v => v.Passes).HasForeignKey(x => x.VisitorVisitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.IssuedByUser).WithMany().HasForeignKey(x => x.IssuedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<SystemSetting>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<VisitPurpose>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<MailContentTemplate>(e =>
        {
            e.HasIndex(x => x.LocationId);
            e.Property(x => x.Subject).IsRequired().HasMaxLength(200);
            e.Property(x => x.Body).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Location>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<IdType>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<FeedbackQuestion>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.SortOrder });
            e.Property(x => x.Prompt).IsRequired().HasMaxLength(300);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<VisitFeedback>(e =>
        {
            e.HasIndex(x => x.VisitorVisitId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SubmittedAt });
            e.Property(x => x.VisitorName).IsRequired().HasMaxLength(150);
            e.Property(x => x.CompanyName).IsRequired().HasMaxLength(200);
            e.Property(x => x.VisitNumber).IsRequired().HasMaxLength(50);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VisitorVisit).WithMany().HasForeignKey(x => x.VisitorVisitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visitor).WithMany().HasForeignKey(x => x.VisitorId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<VisitFeedbackAnswer>(e =>
        {
            e.HasIndex(x => x.VisitFeedbackId);
            e.Property(x => x.QuestionText).IsRequired().HasMaxLength(300);
            e.HasOne(x => x.VisitFeedback).WithMany(f => f.Answers).HasForeignKey(x => x.VisitFeedbackId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.FeedbackQuestion).WithMany().HasForeignKey(x => x.FeedbackQuestionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<VisitorInvitation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => x.HostEmployeeId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.HostEmployee).WithMany().HasForeignKey(x => x.HostEmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductModule>(e => e.HasIndex(x => x.ModuleKey).IsUnique());
        builder.Entity<TenantModuleEntitlement>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ModuleKey }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TenantLicense>(e =>
        {
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<FeatureFlag>(e => e.HasIndex(x => new { x.TenantId, x.Key }));
        builder.Entity<ApplicationRelease>(e => e.HasIndex(x => x.Version));
    }
}
