using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Product;

namespace Tiaano.Vms.Api.Services;

public static class ModuleKeys
{
    public const string VisitorManagement = "visitor-management";
    public const string EmergencyManagement = "emergency-management";
    public const string Analytics = "analytics";
}

public interface IEntitlementService
{
    Guid CurrentTenantId { get; }
    Task EnsureModuleEnabledAsync(string moduleKey);
    Task EnsureLicenseAllowsWriteAsync();
    Task EnsureCanCreateUserAsync();
}

public sealed class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public EntitlementService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public Guid CurrentTenantId =>
        _tenant.TenantId is Guid id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException("Tenant context is required.");

    public async Task EnsureModuleEnabledAsync(string moduleKey)
    {
        var tenantId = CurrentTenantId;
        var entitlement = await _db.TenantModuleEntitlements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ModuleKey == moduleKey);

        if (entitlement is null || !entitlement.IsEnabled)
            throw new UnauthorizedAccessException($"Module '{moduleKey}' is not enabled for this tenant.");

        if (entitlement.ExpiresAt is DateTime exp && exp < DateTime.UtcNow)
            throw new UnauthorizedAccessException($"Module '{moduleKey}' entitlement has expired.");

        await EnsureLicenseAllowsWriteAsync();
    }

    public async Task EnsureLicenseAllowsWriteAsync()
    {
        var tenantId = CurrentTenantId;
        var license = await _db.TenantLicenses.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .OrderByDescending(l => l.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefaultAsync();

        // Missing license: fail closed for multi-tenant product ops (seed always creates one).
        if (license is null)
            throw new UnauthorizedAccessException("No active license for this tenant.");

        if (string.Equals(license.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("License is revoked.");

        if (license.ExpiresAt is DateTime exp)
        {
            var graceEnd = exp.AddDays(Math.Max(0, license.GraceDaysAfterExpiry));
            if (DateTime.UtcNow > graceEnd)
                throw new UnauthorizedAccessException("License has expired.");
        }
    }

    public async Task EnsureCanCreateUserAsync()
    {
        await EnsureLicenseAllowsWriteAsync();
        var tenantId = CurrentTenantId;
        var license = await _db.TenantLicenses.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .OrderByDescending(l => l.ExpiresAt ?? DateTime.MaxValue)
            .FirstAsync();

        var activeUsers = await _db.Users.CountAsync(u => u.IsActive);
        if (activeUsers >= license.MaxUsers)
            throw new InvalidOperationException("User limit for this license has been reached.");
    }
}
