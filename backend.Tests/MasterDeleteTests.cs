using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Security;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Master records can be deleted only by a Super Admin, and only while nothing uses them; the Designation
/// master feeds the employee form and renames carry through to employees.
/// </summary>
[Collection("CoreWorkflow")]
public class MasterDeleteTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MasterDeleteTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Only_A_Super_Admin_Can_Delete_And_Records_In_Use_Are_Refused()
    {
        var superAdmin = await LoginAsync("superadmin");
        var admin = await LoginAsync("admin");

        var designation = await CreateAsync(superAdmin, "/api/masters/designations", $"Test Engineer {Guid.NewGuid():N}"[..30]);
        var forbidden = await admin.DeleteAsync($"/api/masters/designations/{designation}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var deleted = await superAdmin.DeleteAsync($"/api/masters/designations/{designation}");
        Assert.True(deleted.IsSuccessStatusCode, await deleted.Content.ReadAsStringAsync());

        Guid usedDepartment;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            usedDepartment = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.TenantId == WellKnownTenants.TiaanoId)
                .Select(e => e.DepartmentId)
                .FirstAsync();
        }
        var refused = await superAdmin.DeleteAsync($"/api/masters/departments/{usedDepartment}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("cannot be deleted", await refused.Content.ReadAsStringAsync());

        var unused = await CreateAsync(superAdmin, "/api/masters/departments", $"Unused Dept {Guid.NewGuid():N}"[..24]);
        var removed = await superAdmin.DeleteAsync($"/api/masters/departments/{unused}");
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());
        var list = await superAdmin.GetFromJsonAsync<ApiResponse<List<MasterItemDto>>>("/api/masters/departments?activeOnly=false", JsonOptions);
        Assert.DoesNotContain(list!.Data!, d => d.Id == unused);
    }

    [Fact]
    public async Task Renaming_A_Designation_Renames_It_On_Employees()
    {
        var superAdmin = await LoginAsync("superadmin");
        var oldName = $"Shift Lead {Guid.NewGuid():N}"[..24];
        var newName = $"Shift Manager {Guid.NewGuid():N}"[..27];
        var designation = await CreateAsync(superAdmin, "/api/masters/designations", oldName);

        Guid employeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var departmentId = await db.Departments.IgnoreQueryFilters()
                .Where(d => d.TenantId == WellKnownTenants.TiaanoId).Select(d => d.Id).FirstAsync();
            var employee = new Employee
            {
                TenantId = WellKnownTenants.TiaanoId,
                FullName = "Designation Rename Check",
                Designation = oldName,
                DepartmentId = departmentId,
                IsActive = false,
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            employeeId = employee.Id;
        }

        try
        {
            var update = await superAdmin.PutAsJsonAsync($"/api/masters/designations/{designation}", new { name = newName, isActive = true });
            Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Employees.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == employeeId);
            Assert.Equal(newName, stored.Designation);
        }
        finally
        {
            var delete = await superAdmin.DeleteAsync($"/api/employee-master/{employeeId}");
            Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());
            await superAdmin.DeleteAsync($"/api/masters/designations/{designation}");
        }
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string url, string name)
    {
        var response = await client.PostAsJsonAsync(url, new { name, isActive = true });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<MasterItemDto>>(JsonOptions))!.Data!.Id;
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == username);
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, token, TestSecrets.SeedPassword);
            user.MustChangePassword = false;
            user.IsActive = true;
            await users.UpdateAsync(user);
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }
}
