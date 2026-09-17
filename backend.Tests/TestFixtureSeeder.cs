using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Test database configuration.
/// </summary>
/// <remarks>
/// Tests run against their own database rather than the development one. They register visitors,
/// check them in and write audit rows, and pointing them at the working database buried real records
/// under hundreds of fixtures. Set <c>VMS_TEST_CONNECTION</c> to override, e.g. on CI.
/// </remarks>
internal static class TestDatabase
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("VMS_TEST_CONNECTION")
        ?? "Server=localhost;Database=TiaanoVms_Tests;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;Connect Timeout=30";
}

/// <summary>
/// Creates the operator accounts and master data the tests exercise.
/// </summary>
/// <remarks>
/// The application no longer seeds sample departments, hosts or desk logins — a real deployment
/// starts empty and an administrator enters their own. The tests still need something to register a
/// visitor against, so they own those fixtures here instead of depending on production seeding.
/// </remarks>
internal static class TestFixtureSeeder
{
    public const string DepartmentCode = "TESTDEPT";
    public const string HostEmail = "test.host@example.test";

    public static async Task EnsureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tenantId = WellKnownTenants.TiaanoId;

        var department = await db.Departments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Code == DepartmentCode);
        if (department is null)
        {
            department = new Department
            {
                TenantId = tenantId,
                Name = "Test Department",
                Code = DepartmentCode,
                SortOrder = 1,
                CreatedBy = "test"
            };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }

        if (!await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId && e.Email == HostEmail))
        {
            db.Employees.Add(new Employee
            {
                TenantId = tenantId,
                FullName = "Test Host",
                Email = HostEmail,
                DepartmentId = department.Id,
                Intercom = "100",
                Designation = "Test Host",
                CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }

        if (!await db.VisitPurposes.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId && p.Name == "Test Purpose"))
        {
            db.VisitPurposes.Add(new VisitPurpose
            {
                TenantId = tenantId,
                Name = "Test Purpose",
                SortOrder = 1,
                CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }

        await EnsureUserAsync(users, tenantId, "reception", "Reception Desk", AppRoles.Reception, department.Id);
        await EnsureUserAsync(users, tenantId, "security", "Security Desk", AppRoles.Security, department.Id);

        var hostUser = await EnsureUserAsync(users, tenantId, "host", "Test Host", AppRoles.Host, department.Id);
        var hostEmployee = await db.Employees.IgnoreQueryFilters()
            .FirstAsync(e => e.TenantId == tenantId && e.Email == HostEmail);
        if (string.IsNullOrEmpty(hostEmployee.UserId))
        {
            hostEmployee.UserId = hostUser.Id;
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users,
        Guid tenantId,
        string username,
        string fullName,
        string role,
        Guid departmentId)
    {
        var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@example.test",
                EmailConfirmed = true,
                FullName = fullName,
                TenantId = tenantId,
                DepartmentId = departmentId,
                IsActive = true,
                MustChangePassword = false,
                CreatedBy = "test"
            };
            var result = await users.CreateAsync(user, TestSecrets.SeedPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Could not create test user '{username}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        if (!await users.IsInRoleAsync(user, role))
            await users.AddToRoleAsync(user, role);

        return user;
    }
}
