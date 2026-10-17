using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Tests;

internal static class TestUsers
{
    /// <summary>
    /// The test database is shared across runs, and every active user counts against the tenant's
    /// license limit, so tests deactivate the logins they create.
    /// </summary>
    public static async Task DeactivateAsync(TestApiFactory factory, IEnumerable<string> userIds)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return;

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var rows = await users.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ToListAsync();
        foreach (var user in rows)
        {
            user.IsActive = false;
            await users.UpdateAsync(user);
        }
    }

    /// <summary>
    /// An Employees master record without a face, to create a login from: users are only created from employees.
    /// Named to sort after the seeded employees, which other tests pick as the first host by name.
    /// </summary>
    public static async Task<Guid> CreateEmployeeAsync(IServiceProvider services, Guid tenantId, Guid? locationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tiaano.Vms.Api.Data.ApplicationDbContext>();
        var departmentId = await db.Departments.IgnoreQueryFilters()
            .Where(d => d.IsActive && d.TenantId == tenantId && (d.LocationId == null || d.LocationId == locationId))
            .OrderBy(d => d.Name).Select(d => d.Id).FirstAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var employee = new Employee
        {
            TenantId = tenantId,
            FullName = $"Zz Login Employee {suffix}",
            Code = $"LE{suffix}",
            Email = $"login.{suffix}@example.test",
            DepartmentId = departmentId,
            LocationId = locationId,
            IsActive = true
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }
}
