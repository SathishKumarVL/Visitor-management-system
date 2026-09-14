using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

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
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<VisitorPass> VisitorPasses => Set<VisitorPass>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Department>(e =>
        {
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).IsRequired();
        });

        builder.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.FullName);
            e.HasOne(x => x.Department).WithMany(d => d.Employees).HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany(u => u.EmployeeProfiles).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasOne(x => x.Department).WithMany(d => d.Users).HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Visitor>(e =>
        {
            e.HasIndex(x => x.VisitorNumber).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.CompanyName);
        });

        builder.Entity<VisitorVisit>(e =>
        {
            e.HasIndex(x => x.VisitNumber).IsUnique();
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
        });

        builder.Entity<SystemSetting>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
        });

        builder.Entity<VisitPurpose>().HasIndex(x => x.Name);
        builder.Entity<Location>().HasIndex(x => x.Name);

        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
