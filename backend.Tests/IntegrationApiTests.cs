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
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Other applications read reports and push employees with an X-Api-Key created by a SuperAdmin.</summary>
[Collection("CoreWorkflow")]
public class IntegrationApiTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;

    public IntegrationApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Api_Key_Is_Scoped_Isolated_From_Jwt_And_Revocable()
    {
        var superadmin = await LoginAsync("superadmin");
        var admin = await LoginAsync("admin");

        var denied = await admin.PostAsJsonAsync("/api/settings/api-keys", new { name = "Admin try", scopes = new[] { "reports" } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var created = await CreateKeyAsync(superadmin, "Reports only", "reports");
        Assert.StartsWith("vms_", created.PlainKey);
        Assert.Equal(["reports"], created.Key.Scopes);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/integration/reports/visitors")).StatusCode);

        var wrongKey = KeyClient("vms_not-a-real-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("/api/integration/reports/visitors")).StatusCode);

        var reportsClient = KeyClient(created.PlainKey);
        var report = await reportsClient.GetAsync("/api/integration/reports/visitors?reportType=daily");
        Assert.True(report.IsSuccessStatusCode, await report.Content.ReadAsStringAsync());
        var data = (await report.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("rows").ValueKind);
        Assert.Equal("api:Reports only", data.GetProperty("generatedBy").GetString());

        var csv = await reportsClient.GetAsync("/api/integration/reports/visitors?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.Forbidden, (await reportsClient.GetAsync("/api/integration/employees")).StatusCode);

        // A key never works outside /integration, and a user token never works inside it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await reportsClient.GetAsync("/api/visitors?page=1&pageSize=5")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await superadmin.GetAsync("/api/integration/reports/visitors")).StatusCode);

        var revoke = await superadmin.PostAsync($"/api/settings/api-keys/{created.Key.Id}/revoke", null);
        Assert.True(revoke.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reportsClient.GetAsync("/api/integration/reports/visitors")).StatusCode);

        var list = await superadmin.GetFromJsonAsync<ApiResponse<List<IntegrationApiKeyDto>>>("/api/settings/api-keys", JsonOptions);
        var listed = list!.Data!.Single(k => k.Id == created.Key.Id);
        Assert.False(listed.IsActive);
        Assert.NotNull(listed.LastUsedAt);
    }

    [Fact]
    public async Task Employee_Push_Creates_Updates_And_Reports_Bad_Rows()
    {
        var superadmin = await LoginAsync("superadmin");
        var key = await CreateKeyAsync(superadmin, "HR system", "employees");
        var client = KeyClient(key.PlainKey);

        string departmentName;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            departmentName = await db.Departments.IgnoreQueryFilters()
                .Where(d => d.IsActive && d.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(d => d.Name).Select(d => d.Name).FirstAsync();
        }

        var code = $"HR{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var first = await client.PostAsJsonAsync("/api/integration/employees", new
        {
            employees = new object[]
            {
                new { code, fullName = "Integration Employee", department = departmentName, designation = "Engineer", email = $"{code.ToLowerInvariant()}@example.test" },
                new { code = code + "X", fullName = "Unknown Dept", department = "No Such Department 123" },
                new { code, fullName = "Duplicate Code", department = departmentName },
            }
        });
        var firstResult = (await first.Content.ReadFromJsonAsync<ApiResponse<IntegrationEmployeeBatchResult>>(JsonOptions))!;
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(firstResult.Success);
        Assert.Equal(1, firstResult.Data!.Created);
        Assert.Equal(2, firstResult.Data.Failed);
        Assert.Contains(firstResult.Data.Errors, e => e.Index == 1 && e.Message.Contains("was not found"));
        Assert.Contains(firstResult.Data.Errors, e => e.Index == 2 && e.Message.Contains("more than once"));

        var second = await client.PostAsJsonAsync("/api/integration/employees", new
        {
            employees = new[] { new { code, fullName = "Integration Employee", department = departmentName, designation = "Lead Engineer", active = false } }
        });
        var secondResult = (await second.Content.ReadFromJsonAsync<ApiResponse<IntegrationEmployeeBatchResult>>(JsonOptions))!;
        Assert.True(secondResult.Success, secondResult.Message);
        Assert.Equal(1, secondResult.Data!.Updated);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var employees = await db.Employees.IgnoreQueryFilters().Where(e => e.Code == code).ToListAsync();
            var employee = Assert.Single(employees);
            Assert.False(employee.IsActive);
            Assert.Equal("Lead Engineer", employee.Designation);
            Assert.Equal($"{code.ToLowerInvariant()}@example.test", employee.Email);
        }

        var activeOnly = await client.GetFromJsonAsync<ApiResponse<List<IntegrationEmployeeDto>>>("/api/integration/employees", JsonOptions);
        Assert.DoesNotContain(activeOnly!.Data!, e => e.Code == code);
        var all = await client.GetFromJsonAsync<ApiResponse<List<IntegrationEmployeeDto>>>("/api/integration/employees?includeInactive=true", JsonOptions);
        Assert.Contains(all!.Data!, e => e.Code == code && e.Department == departmentName && !e.Active);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/integration/reports/visitors")).StatusCode);
        Assert.True((await client.GetAsync("/api/integration/masters")).IsSuccessStatusCode);
    }

    private HttpClient KeyClient(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<CreatedIntegrationApiKeyDto> CreateKeyAsync(HttpClient superadmin, string name, params string[] scopes)
    {
        var response = await superadmin.PostAsJsonAsync("/api/settings/api-keys", new { name, scopes });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CreatedIntegrationApiKeyDto>>(JsonOptions))!.Data!;
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
