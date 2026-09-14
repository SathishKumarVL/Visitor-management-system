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

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        if (!await context.Departments.AnyAsync())
        {
            var departments = new (string Name, string Code, int Order)[]
            {
                ("Managing Director", "MD", 1),
                ("Administration", "ADMIN", 2),
                ("Production", "PROD", 3),
                ("Quality Control", "QC", 4),
                ("Purchase", "PUR", 5),
                ("Design", "DES", 6),
                ("Logistics", "LOG", 7),
                ("Corporate Finance", "CFIN", 8),
                ("Marketing", "MKT", 9),
                ("Facilitation", "FAC", 10),
                ("Finance", "FIN", 11),
                ("MSE", "MSE", 12)
            };

            foreach (var d in departments)
            {
                context.Departments.Add(new Department
                {
                    Name = d.Name,
                    Code = d.Code,
                    SortOrder = d.Order,
                    CreatedBy = "system"
                });
            }
            await context.SaveChangesAsync();
        }

        if (!await context.VisitPurposes.AnyAsync())
        {
            var purposes = new[]
            {
                "Equipments", "Chlor Alkali", "DSA Anode", "Chlorinator", "Composite",
                "H2 Generator", "HOCl Generator", "Platinized Anode", "Electrolytic Scale Remover",
                "ScaleX", "Cathodic Protection", "S.A.E.W.T / T'Chlor",
                "Effluent Treatment Plant / Sewage Treatment Plant",
                "Others"
            };
            for (var i = 0; i < purposes.Length; i++)
            {
                context.VisitPurposes.Add(new VisitPurpose
                {
                    Name = purposes[i],
                    SortOrder = i + 1,
                    CreatedBy = "system"
                });
            }
            await context.SaveChangesAsync();
        }

        await VisitPurposeDefaults.EnsureOthersPurposeAsync(context);

        if (!await context.Locations.AnyAsync())
        {
            var locations = new (string Name, bool Plant, bool Other, int Order)[]
            {
                ("Anode Hall", false, false, 1),
                ("Titanium Hall", false, false, 2),
                ("Nickel Hall", false, false, 3),
                ("Tantalum Hall", false, false, 4),
                ("Reception", false, false, 5),
                ("Zirconium Hall", false, false, 6),
                ("Platinum Hall", false, false, 7),
                ("Composite Hall", false, false, 8),
                ("Plant No.", true, false, 9),
                ("Others", false, true, 10)
            };
            foreach (var loc in locations)
            {
                context.Locations.Add(new Location
                {
                    Name = loc.Name,
                    RequiresPlantNumber = loc.Plant,
                    RequiresOtherText = loc.Other,
                    SortOrder = loc.Order,
                    CreatedBy = "system"
                });
            }
            await context.SaveChangesAsync();
        }

        if (!await context.IdTypes.AnyAsync())
        {
            foreach (var (name, order) in new[] { ("Aadhaar", 1), ("PAN", 2), ("Driving License", 3), ("Passport", 4), ("Voter ID", 5), ("Company ID", 6) })
            {
                context.IdTypes.Add(new IdType { Name = name, SortOrder = order, CreatedBy = "system" });
            }
            await context.SaveChangesAsync();
        }

        if (!await context.EntryGates.AnyAsync())
        {
            context.EntryGates.Add(new EntryGate { Name = "Main Gate", IsDefault = true, CreatedBy = "system" });
            context.EntryGates.Add(new EntryGate { Name = "Reception Entrance", CreatedBy = "system" });
            context.ExitGates.Add(new ExitGate { Name = "Main Gate", IsDefault = true, CreatedBy = "system" });
            context.ExitGates.Add(new ExitGate { Name = "Reception Exit", CreatedBy = "system" });
            await context.SaveChangesAsync();
        }

        if (!await context.SystemSettings.AnyAsync())
        {
            var defaults = new Dictionary<string, string>
            {
                ["CompanyName"] = "TIAANO",
                ["LogoPath"] = "/branding/tiaano-logo.png",
                ["VisitorIdPrefix"] = "TIA",
                ["VisitorPassValidityHours"] = "12",
                ["ApprovalRequired"] = "false",
                ["WalkInApprovalRequired"] = "false",
                ["PhotoRequired"] = "false",
                ["IdVerificationRequired"] = "false",
                ["MaxVisitDurationWarningMinutes"] = "240",
                ["DefaultEntryGate"] = "Main Gate",
                ["DefaultExitGate"] = "Main Gate",
                ["SessionTimeoutMinutes"] = "480"
            };
            foreach (var kv in defaults)
            {
                context.SystemSettings.Add(new SystemSetting
                {
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
            var setting = await context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting is null)
            {
                context.SystemSettings.Add(new SystemSetting { Key = key, Value = "false", UpdatedBy = "system" });
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
        var pendingVisits = await context.VisitorVisits
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

        var adminDept = await context.Departments.FirstAsync(d => d.Code == "ADMIN");
        var productionDept = await context.Departments.FirstAsync(d => d.Code == "PROD");
        var mdDept = await context.Departments.FirstAsync(d => d.Code == "MD");

        if (!await context.Employees.AnyAsync())
        {
            context.Employees.AddRange(
                new Employee { FullName = "Ramesh Kumar", Email = "ramesh.host@tiaano.local", DepartmentId = productionDept.Id, Intercom = "201", Designation = "Production Manager", CreatedBy = "system" },
                new Employee { FullName = "Priya Sharma", Email = "priya.host@tiaano.local", DepartmentId = adminDept.Id, Intercom = "101", Designation = "Admin Manager", CreatedBy = "system" },
                new Employee { FullName = "Anil Mehta", Email = "anil.md@tiaano.local", DepartmentId = mdDept.Id, Intercom = "001", Designation = "Managing Director", CreatedBy = "system" },
                new Employee { FullName = "Sneha Patel", Email = "sneha.qc@tiaano.local", DepartmentId = (await context.Departments.FirstAsync(d => d.Code == "QC")).Id, Intercom = "301", Designation = "QC Lead", CreatedBy = "system" }
            );
            await context.SaveChangesAsync();
        }

        var seedPassword = SecretConfiguration.GetSeedPasswordForCreateOnly(config);
        await EnsureUserAsync(userManager, context, "superadmin", "Super Administrator", "superadmin@tiaano.local", AppRoles.SuperAdmin, adminDept.Id, seedPassword, false);
        await EnsureUserAsync(userManager, context, "admin", "System Admin", "admin@tiaano.local", AppRoles.Admin, adminDept.Id, seedPassword, true);
        await EnsureUserAsync(userManager, context, "reception", "Reception Desk", "reception@tiaano.local", AppRoles.Reception, adminDept.Id, seedPassword, true);
        await EnsureUserAsync(userManager, context, "security", "Security Desk", "security@tiaano.local", AppRoles.Security, adminDept.Id, seedPassword, true);

        var hostUser = await EnsureUserAsync(userManager, context, "host", "Ramesh Kumar", "ramesh.host@tiaano.local", AppRoles.Host, productionDept.Id, seedPassword, true);
        var hostEmp = await context.Employees.FirstOrDefaultAsync(e => e.Email == "ramesh.host@tiaano.local");
        if (hostEmp is not null && string.IsNullOrEmpty(hostEmp.UserId))
        {
            hostEmp.UserId = hostUser.Id;
            await context.SaveChangesAsync();
        }
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
        string username,
        string fullName,
        string email,
        string role,
        Guid departmentId,
        string? password,
        bool mustChangePassword)
    {
        var user = await userManager.FindByNameAsync(username);
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
                DepartmentId = departmentId,
                IsActive = true,
                MustChangePassword = mustChangePassword,
                CreatedBy = "system"
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create user {username}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
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

    public static async Task<VisitPurpose> EnsureOthersPurposeAsync(ApplicationDbContext context)
    {
        var existing = await context.VisitPurposes.FirstOrDefaultAsync(p => p.Name == OthersName);
        if (existing != null) return existing;

        var maxOrder = await context.VisitPurposes.MaxAsync(p => (int?)p.SortOrder) ?? 0;
        var created = new VisitPurpose
        {
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
