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
using Tiaano.Vms.Api.Models.Enums;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Employees get their own Host login, see only their visitors and add minutes of meeting.</summary>
[Collection("CoreWorkflow")]
public class HostLoginTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;
    private readonly List<string> _createdUserIds = new();

    public HostLoginTests(TestApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => TestUsers.DeactivateAsync(_factory, _createdUserIds);

    [Fact]
    public async Task SuperAdmin_Creates_Login_Once_And_Admin_Cannot()
    {
        var employee = await CreateEmployeeAsync();
        var superadmin = await LoginAsync("superadmin");

        var username = $"host{Guid.NewGuid():N}"[..20];
        var created = await CreateLoginAsync(superadmin, employee.Id, username);
        Assert.Equal(username, created.UserName);
        Assert.False(string.IsNullOrWhiteSpace(created.UserId));

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == created.UserId);
            Assert.Equal(employee.DepartmentId, user.DepartmentId);
            Assert.True(user.MustChangePassword);
            Assert.Contains(AppRoles.Host, await users.GetRolesAsync(user));
        }

        var again = await superadmin.PostAsJsonAsync($"/api/users/from-employee/{employee.Id}",
            new { username = username + "x", password = TestSecrets.SeedPassword });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("already has a login", await again.Content.ReadAsStringAsync());

        var other = await CreateEmployeeAsync();
        var admin = await LoginAsync("admin");
        var denied = await admin.PostAsJsonAsync($"/api/users/from-employee/{other.Id}",
            new { username = $"denied{Guid.NewGuid():N}"[..20], password = TestSecrets.SeedPassword });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var list = await superadmin.GetFromJsonAsync<ApiResponse<List<EmployeeDto>>>("/api/masters/hosts?activeOnly=false", JsonOptions);
        Assert.Equal(username, list!.Data!.Single(e => e.Id == employee.Id).UserName);
    }

    [Fact]
    public async Task Host_Sees_Only_Own_Visitors_And_Adds_Minutes()
    {
        var mine = await CreateEmployeeAsync();
        var someoneElse = await CreateEmployeeAsync();
        var superadmin = await LoginAsync("superadmin");
        var hostUsername = $"host{Guid.NewGuid():N}"[..20];
        await CreateLoginAsync(superadmin, mine.Id, hostUsername);

        var myVisit = await RegisterAndCheckInAsync(mine, "Host Own Visitor");
        var otherVisit = await RegisterAndCheckInAsync(someoneElse, "Host Other Visitor");

        var host = await LoginAsync(hostUsername);

        var inside = await host.GetFromJsonAsync<ApiResponse<List<VisitorListItemDto>>>("/api/visitors/inside", JsonOptions);
        var insideIds = inside!.Data!.Select(v => v.VisitId).ToList();
        Assert.Contains(myVisit, insideIds);
        Assert.DoesNotContain(otherVisit, insideIds);

        (await superadmin.PostAsJsonAsync($"/api/visitors/{myVisit}/check-out", new { skipFeedback = true })).EnsureSuccessStatusCode();
        (await superadmin.PostAsJsonAsync($"/api/visitors/{otherVisit}/check-out", new { skipFeedback = true })).EnsureSuccessStatusCode();

        var dashboard = (await host.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions))!.Data!;
        Assert.True(dashboard.IsHostView);
        Assert.Contains(dashboard.MomPendingItems, v => v.VisitId == myVisit);
        Assert.DoesNotContain(dashboard.MomPendingItems, v => v.VisitId == otherVisit);
        Assert.DoesNotContain(dashboard.RecentVisitors, v => v.VisitId == otherVisit);

        var forbidden = await host.PutAsJsonAsync($"/api/visitors/{otherVisit}/meeting-minutes", new { meetingMinutes = "Not my meeting." });
        Assert.False(forbidden.IsSuccessStatusCode);

        var save = await host.PutAsJsonAsync($"/api/visitors/{myVisit}/meeting-minutes", new { meetingMinutes = "Reviewed the proposal." });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

        var after = (await host.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions))!.Data!;
        Assert.DoesNotContain(after.MomPendingItems, v => v.VisitId == myVisit);

        var adminDashboard = (await superadmin.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions))!.Data!;
        Assert.False(adminDashboard.IsHostView);
    }

    private async Task<EmployeeDto> CreateLoginAsync(HttpClient superadmin, Guid employeeId, string username)
    {
        var response = await superadmin.PostAsJsonAsync($"/api/users/from-employee/{employeeId}",
            new { username, password = TestSecrets.SeedPassword });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var employee = (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
        if (employee.UserId is not null) _createdUserIds.Add(employee.UserId);
        return employee;
    }

    private async Task<EmployeeDto> CreateEmployeeAsync()
    {
        Guid departmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            departmentId = await db.Departments.IgnoreQueryFilters()
                .Where(d => d.IsActive && d.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(d => d.Name).Select(d => d.Id).FirstAsync();
        }

        var superadmin = await LoginAsync("superadmin");
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var response = await superadmin.PostAsJsonAsync("/api/masters/hosts", new
        {
            fullName = $"Host Login {suffix}",
            email = $"host.{suffix}@example.test",
            departmentId,
            isActive = true
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
    }

    private async Task<Guid> RegisterAndCheckInAsync(EmployeeDto host, string namePrefix)
    {
        var admin = await LoginAsync("admin");
        var settings = (await (await admin.GetAsync("/api/settings")).Content
            .ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        settings.ApprovalRequired = false;
        settings.WalkInApprovalRequired = false;
        (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();

        Guid purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            purposeId = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync();
        }

        var name = $"{namePrefix} {Guid.NewGuid():N}"[..40];
        var reception = await LoginAsync("reception");
        var register = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            gender = 1,
            companyName = "Host Login Co",
            telephone = $"97{Random.Shared.Next(10000000, 99999999)}",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId = host.DepartmentId,
            hostEmployeeId = host.Id,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            photoBase64 = TestPhotos.Base64(name),
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(register.IsSuccessStatusCode, await register.Content.ReadAsStringAsync());
        var data = (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var visitId = data.GetProperty("visitId").GetGuid();

        if (!string.Equals(data.GetProperty("statusLabel").GetString(), "Inside", StringComparison.OrdinalIgnoreCase))
        {
            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
        }
        return visitId;
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == username);
            var reset = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, reset, TestSecrets.SeedPassword);
            user.MustChangePassword = false;
            user.IsActive = true;
            await users.UpdateAsync(user);
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }
}
