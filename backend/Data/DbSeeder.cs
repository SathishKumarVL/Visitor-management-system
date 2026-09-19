using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Configuration;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await context.Database.MigrateAsync();
        await EnsureColumnWidthsAsync(context);

        var tenant = await EnsureDefaultTenantAsync(context);
        var tenantId = tenant.Id;

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Backfill tenant ids for legacy rows (idempotent).
        await BackfillTenantIdsAsync(context, tenantId);

        // Departments, visit purposes, locations, ID types and gates are deliberately not seeded.
        // They describe one particular organisation, so a fresh install starts empty and an
        // administrator enters their own under Admin -> Masters. The only exception is below:
        // "Others" is a fallback the registration flow depends on, so it is a system record rather
        // than sample content. VisitorService recreates it on demand if it is ever deleted.
        await VisitPurposeDefaults.EnsureOthersPurposeAsync(context, tenantId);
        await FeedbackQuestionDefaults.EnsureDefaultsAsync(context, tenantId);

        if (!await context.SystemSettings.IgnoreQueryFilters().AnyAsync())
        {
            var defaults = new Dictionary<string, string>
            {
                ["CompanyName"] = "TIAANO",
                ["LogoPath"] = "/branding/tiaano-logo.png",
                ["VisitorIdPrefix"] = "VMS",
                ["VisitorPassValidityHours"] = "12",
                ["ApprovalRequired"] = "false",
                ["WalkInApprovalRequired"] = "false",
                ["PhotoRequired"] = "false",
                ["IdVerificationRequired"] = "false",
                ["MaxVisitDurationWarningMinutes"] = "240",
                ["SessionTimeoutMinutes"] = "480",
                ["ThemePreset"] = "tiaano",
                ["FontPreset"] = "inter"
            };
            foreach (var kv in defaults)
            {
                context.SystemSettings.Add(new SystemSetting
                {
                    TenantId = tenantId,
                    Key = kv.Key,
                    Value = kv.Value,
                    UpdatedBy = "system"
                });
            }
            await context.SaveChangesAsync();
        }

        // Reception walk-ins must not wait for host approval — force off for existing DBs too.
        foreach (var key in new[] { "ApprovalRequired", "WalkInApprovalRequired" })
        {
            var setting = await context.SystemSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Key == key && s.TenantId == tenantId);
            if (setting is null)
            {
                context.SystemSettings.Add(new SystemSetting { TenantId = tenantId, Key = key, Value = "false", UpdatedBy = "system" });
            }
            else if (!string.Equals(setting.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                setting.Value = "false";
                setting.UpdatedAt = DateTime.UtcNow;
                setting.UpdatedBy = "system";
            }
        }
        await context.SaveChangesAsync();

        // Clear any leftover pending-approval visits (approvals are disabled).
        var pendingVisits = await context.VisitorVisits.IgnoreQueryFilters()
            .Where(v => v.Status == VisitStatus.PendingApproval)
            .ToListAsync();
        if (pendingVisits.Count > 0)
        {
            foreach (var visit in pendingVisits)
            {
                visit.Status = VisitStatus.Approved;
                visit.UpdatedAt = DateTime.UtcNow;
                visit.UpdatedBy = "system";
            }
            await context.SaveChangesAsync();
        }

        // Only the two accounts needed to get in and configure the system. Reception, security and
        // host accounts belong to real people and are created by an administrator under Admin ->
        // Users, so that every operator action in the audit trail names someone accountable.
        var seedPassword = SecretConfiguration.GetSeedPasswordForCreateOnly(config);
        await EnsureUserAsync(userManager, context, tenantId, "superadmin", "Super Administrator", "superadmin@tiaano.local", AppRoles.SuperAdmin, null, seedPassword, false);
        await EnsureUserAsync(userManager, context, tenantId, "admin", "System Admin", "admin@tiaano.local", AppRoles.Admin, null, seedPassword, true);

        await EnsureProductFoundationAsync(context, tenantId);
        await EnsurePassNumberSeriesAsync(context, tenantId);
        await EnsureReminderSettingAsync(context, tenantId);
    }

    private static async Task EnsurePassNumberSeriesAsync(ApplicationDbContext context, Guid tenantId)
    {
        if (await context.PassNumberSeries.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenantId))
            return;

        context.PassNumberSeries.Add(new PassNumberSeries
        {
            TenantId = tenantId,
            Prefix = "VMS-",
            Year = null,
            StartNumber = 1000,
            EndNumber = 999999,
            CurrentNumber = 999,
            IsActive = true
        });
        await context.SaveChangesAsync();
    }

    private static async Task EnsureReminderSettingAsync(ApplicationDbContext context, Guid tenantId)
    {
        if (await context.SystemSettings.IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenantId && s.Key == "ReminderHoursBefore"))
            return;

        context.SystemSettings.Add(new SystemSetting
        {
            TenantId = tenantId,
            Key = "ReminderHoursBefore",
            Value = "24,1",
            Description = "Comma-separated hours before an expected visit to send reminders",
            UpdatedBy = "system"
        });
        await context.SaveChangesAsync();
    }

    private static async Task EnsureProductFoundationAsync(ApplicationDbContext context, Guid tenantId)
    {
        if (!await context.ProductModules.AnyAsync())
        {
            context.ProductModules.AddRange(
                new Models.Product.ProductModule { ModuleKey = "visitor-management", Name = "Visitor Management", IsCore = true, Version = "1.0.0" },
                new Models.Product.ProductModule { ModuleKey = "emergency-management", Name = "Emergency Management", IsCore = false, Version = "0.1.0" },
                new Models.Product.ProductModule { ModuleKey = "contractor-management", Name = "Contractor Management", IsCore = false, Version = "0.0.0" },
                new Models.Product.ProductModule { ModuleKey = "analytics", Name = "Analytics", IsCore = false, Version = "0.0.0" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.TenantLicenses.AnyAsync(l => l.TenantId == tenantId))
        {
            context.TenantLicenses.Add(new Models.Product.TenantLicense
            {
                TenantId = tenantId,
                Edition = "Professional",
                MaxSites = 5,
                MaxUsers = 100,
                IsActive = true,
                Status = "Active",
                GraceDaysAfterExpiry = 30
            });
        }

        foreach (var key in new[] { "visitor-management", "emergency-management" })
        {
            if (!await context.TenantModuleEntitlements.AnyAsync(m => m.TenantId == tenantId && m.ModuleKey == key))
            {
                context.TenantModuleEntitlements.Add(new Models.Product.TenantModuleEntitlement
                {
                    TenantId = tenantId,
                    ModuleKey = key,
                    IsEnabled = true
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task<Tenant> EnsureDefaultTenantAsync(ApplicationDbContext context)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Id == WellKnownTenants.TiaanoId)
                     ?? await context.Tenants.FirstOrDefaultAsync(t => t.Code == WellKnownTenants.TiaanoCode);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = WellKnownTenants.TiaanoId,
                Code = WellKnownTenants.TiaanoCode,
                Name = "TIAANO",
                LogoPath = "/branding/tiaano-logo.png",
                PrimaryColor = "#0F766E",
                SecondaryColor = "#14B8A6",
                IsActive = true
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        if (!await context.Sites.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenant.Id))
        {
            context.Sites.Add(new Site
            {
                TenantId = tenant.Id,
                Name = "Headquarters",
                Code = "HQ",
                IsDefault = true,
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        return tenant;
    }

    private static async Task BackfillTenantIdsAsync(ApplicationDbContext context, Guid tenantId)
    {
        await context.Database.ExecuteSqlRawAsync("""
            UPDATE AspNetUsers SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000' OR TenantId IS NULL;
            UPDATE Departments SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE Employees SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE VisitPurposes SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE Locations SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE IdTypes SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000' OR TenantId IS NULL;
            UPDATE Visitors SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE VisitorVisits SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE SystemSettings SET TenantId = {0} WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            UPDATE NotificationOutbox SET TenantId = {0} WHERE TenantId IS NULL;
            """, tenantId);

        await BackfillSiteIdsAsync(context, tenantId);
    }

    /// <summary>
    /// Site scoping filters visits and areas, so anything created before sites existed has to be adopted by
    /// the tenant default. Without this, a site-bound user would see an empty system.
    /// </summary>
    private static async Task BackfillSiteIdsAsync(ApplicationDbContext context, Guid tenantId)
    {
        var defaultSiteId = await context.Sites.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();

        if (defaultSiteId is not Guid siteId) return;

        await context.Database.ExecuteSqlRawAsync("""
            UPDATE VisitorVisits SET SiteId = {1} WHERE TenantId = {0} AND SiteId IS NULL;
            UPDATE Locations SET SiteId = {1} WHERE TenantId = {0} AND SiteId IS NULL;
            """, tenantId, siteId);
    }

    private static async Task EnsureColumnWidthsAsync(ApplicationDbContext context)
    {
        // Widen ID columns without a full migration so existing local DBs stop truncating.
        await context.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('dbo.VisitorDocuments', 'IdNumberMasked') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.VisitorDocuments ALTER COLUMN IdNumberMasked nvarchar(50) NOT NULL;
            END
            IF COL_LENGTH('dbo.VisitorDocuments', 'IdNumberEncrypted') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.VisitorDocuments ALTER COLUMN IdNumberEncrypted nvarchar(500) NOT NULL;
            END
            IF COL_LENGTH('dbo.VisitorVisits', 'NumberOfPersons') IS NULL
            BEGIN
                ALTER TABLE dbo.VisitorVisits ADD NumberOfPersons int NOT NULL CONSTRAINT DF_VisitorVisits_NumberOfPersons DEFAULT 1;
            END
            """);
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        Guid tenantId,
        string username,
        string fullName,
        string email,
        string role,
        Guid? departmentId,
        string? password,
        bool mustChangePassword)
    {
        var user = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Seed user '{username}' does not exist and Seed:DefaultPassword is not configured. " +
                    "Set Seed:DefaultPassword via User Secrets or environment for first-time create only.");
            }

            user = new ApplicationUser
            {
                UserName = username,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                TenantId = tenantId,
                DepartmentId = departmentId,
                IsActive = true,
                MustChangePassword = mustChangePassword,
                CreatedBy = "system"
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create user {username}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
        else if (user.TenantId == Guid.Empty)
        {
            user.TenantId = tenantId;
            await userManager.UpdateAsync(user);
        }
        // Existing users: never reset passwords on startup.

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);

        return user;
    }
}

public static class VisitPurposeDefaults
{
    public const string OthersName = "Others";

    public static Task<VisitPurpose> EnsureOthersPurposeAsync(ApplicationDbContext context) =>
        EnsureOthersPurposeAsync(context, WellKnownTenants.TiaanoId);

    public static async Task<VisitPurpose> EnsureOthersPurposeAsync(ApplicationDbContext context, Guid tenantId)
    {
        var existing = await context.VisitPurposes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Name == OthersName && p.TenantId == tenantId);
        if (existing != null) return existing;

        var maxOrder = await context.VisitPurposes.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .MaxAsync(p => (int?)p.SortOrder) ?? 0;
        var created = new VisitPurpose
        {
            TenantId = tenantId,
            Name = OthersName,
            SortOrder = maxOrder + 1,
            IsActive = true,
            CreatedBy = "system"
        };
        context.VisitPurposes.Add(created);
        await context.SaveChangesAsync();
        return created;
    }
}

public static class FeedbackQuestionDefaults
{
    private static readonly string[] DefaultPrompts =
    [
        "Overall visit experience",
        "Reception / front desk service",
        "Ease of check-in process",
    ];

    public static async Task EnsureDefaultsAsync(ApplicationDbContext context, Guid tenantId)
    {
        var any = await context.FeedbackQuestions.IgnoreQueryFilters()
            .AnyAsync(q => q.TenantId == tenantId);
        if (any) return;

        for (var i = 0; i < DefaultPrompts.Length; i++)
        {
            context.FeedbackQuestions.Add(new FeedbackQuestion
            {
                TenantId = tenantId,
                Prompt = DefaultPrompts[i],
                IsRequired = true,
                IsActive = true,
                SortOrder = i + 1,
                CreatedBy = "system"
            });
        }
        await context.SaveChangesAsync();
    }
}

public static class SensitiveDataHelper
{
    public const int MaskedIdMaxLength = 20;

    public static string MaskId(string idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber)) return string.Empty;
        var clean = idNumber.Trim();
        if (clean.Length <= 4)
            return new string('*', Math.Min(clean.Length, MaskedIdMaxLength));

        // Keep last 4 characters visible; cap total length to the DB column size.
        var visible = clean[^4..];
        var starCount = Math.Min(MaskedIdMaxLength - visible.Length, Math.Max(4, clean.Length - 4));
        starCount = Math.Clamp(starCount, 1, MaskedIdMaxLength - visible.Length);
        return new string('*', starCount) + visible;
    }

    public static string Encrypt(string plainText, string key)
    {
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
    }

    public static string Decrypt(string cipherText, string key)
    {
        var all = Convert.FromBase64String(cipherText);
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = all[..16];
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(all, 16, all.Length - 16);
        return Encoding.UTF8.GetString(plain);
    }
}
