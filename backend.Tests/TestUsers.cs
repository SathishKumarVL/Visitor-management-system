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
}
