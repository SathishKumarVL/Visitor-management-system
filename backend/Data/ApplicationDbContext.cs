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
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<VisitPurpose> VisitPurposes => Set<VisitPurpose>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<IdType> IdTypes => Set<IdType>();
    public DbSet<EntryGate> EntryGates => Set<EntryGate>();
    public DbSet<ExitGate> ExitGates => Set<ExitGate>();
    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<VisitorVisit> VisitorVisits => Set<VisitorVisit>();
    public DbSet<VisitorVisitPurpose> VisitorVisitPurposes => Set<VisitorVisitPurpose>();
    public DbSet<VisitorVisitLocation> VisitorVisitLocations => Set<VisitorVisitLocation>();
    public DbSet<VisitorPhoto> VisitorPhotos => Set<VisitorPhoto>();
    public DbSet<VisitorDocument> VisitorDocuments => Set<VisitorDocument>();
    public DbSet<VisitorFaceDescriptor> VisitorFaceDescriptors => Set<VisitorFaceDescriptor>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<VisitorPass> VisitorPasses => Set<VisitorPass>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProductModule> ProductModules => Set<ProductModule>();
    public DbSet<TenantModuleEntitlement> TenantModuleEntitlements => Set<TenantModuleEntitlement>();
    public DbSet<TenantLicense> TenantLicenses => Set<TenantLicense>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<ApplicationRelease> ApplicationReleases => Set<ApplicationRelease>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired();
        });

        builder.Entity<Site>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code });
            e.HasOne(x => x.Tenant).WithMany(t => t.Sites).HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Department>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.Property(x => x.Name).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Fail closed: when a scoped tenant context exists without TenantId, return no rows (never all tenants).
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.FullName);
            e.HasOne(x => x.Department).WithMany(d => d.Employees).HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.VisitDate);
            e.HasIndex(x => x.PreRegistrationReference);
            e.HasOne(x => x.Visitor).WithMany(v => v.Visits).HasForeignKey(x => x.VisitorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.HostEmployee).WithMany().HasForeignKey(x => x.HostEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EntryGate).WithMany().HasForeignKey(x => x.EntryGateId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ExitGate).WithMany().HasForeignKey(x => x.ExitGateId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CheckedInByUser).WithMany().HasForeignKey(x => x.CheckedInByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.SecurityCheckInUser).WithMany().HasForeignKey(x => x.SecurityCheckInUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CheckedOutByUser).WithMany().HasForeignKey(x => x.CheckedOutByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
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

        builder.Entity<NotificationOutbox>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IsSent });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            // Fail closed: a queued notification is only visible to the tenant that queued it.
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
            e.HasIndex(x => x.PassCode).IsUnique();
            e.HasOne(x => x.VisitorVisit).WithMany(v => v.Passes).HasForeignKey(x => x.VisitorVisitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.IssuedByUser).WithMany().HasForeignKey(x => x.IssuedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Entity);
            e.HasIndex(x => x.Action);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && (x.TenantId == null || x.TenantId == _tenant.TenantId)));
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

        builder.Entity<EntryGate>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenant == null || (_tenant.TenantId != null && x.TenantId == _tenant.TenantId));
        });

        builder.Entity<ExitGate>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
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
