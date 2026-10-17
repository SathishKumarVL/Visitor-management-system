using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Visitor numbers carry the visit's date; the dashboard's manual check-out card has a matching list.</summary>
[Collection("CoreWorkflow")]
public class EntryNumberAndFilterTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;

    public EntryNumberAndFilterTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Returning_Visitor_Gets_Todays_Date_In_Visitor_Number()
    {
        var admin = await LoginAsync("superadmin");
        await DisableApprovalsAsync(admin);
        var today = AppTime.Today.ToString("yyyyMMdd");

        var name = $"Entry Number {Guid.NewGuid():N}"[..30];
        var first = await RegisterAsync(admin, name, recognizedVisitorId: null);
        Assert.Contains($"-V-{today}-", first.VisitorNumber);

        // Pretend the first visit happened on an earlier day.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visitor = await db.Visitors.IgnoreQueryFilters().FirstAsync(v => v.Id == first.VisitorId);
            visitor.VisitorNumber = $"VMS-V-20260923-{Random.Shared.Next(1000, 9999)}-{Guid.NewGuid():N}"[..28];
            var visit = await db.VisitorVisits.IgnoreQueryFilters().FirstAsync(v => v.Id == first.VisitId);
            visit.EntryNumber = null;
            visit.Status = Models.Enums.VisitStatus.CheckedOut;
            visit.CheckInAt ??= DateTime.UtcNow.AddHours(-2);
            visit.CheckOutAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        var second = await RegisterAsync(admin, name, recognizedVisitorId: first.VisitorId);
        Assert.Equal(first.VisitorId, second.VisitorId);
        Assert.Contains($"-V-{today}-", second.VisitorNumber);
        Assert.DoesNotContain("20260923", second.VisitorNumber);

        var search = await admin.GetFromJsonAsync<ApiResponse<PagedResult<VisitorListItemDto>>>(
            $"/api/visitors?query={Uri.EscapeDataString(second.VisitorNumber)}", JsonOptions);
        Assert.Contains(search!.Data!.Items, v => v.VisitId == second.VisitId);
    }

    [Fact]
    public async Task Manual_Checkout_Quick_Filter_Lists_Todays_Manual_Checkouts()
    {
        var admin = await LoginAsync("superadmin");
        await DisableApprovalsAsync(admin);
        var superadmin = await LoginAsync("superadmin");

        var visit = await RegisterAsync(admin, $"Manual Out {Guid.NewGuid():N}"[..30], recognizedVisitorId: null);
        if (!visit.Inside)
            (await admin.PostAsJsonAsync($"/api/visitors/{visit.VisitId}/check-in", new { })).EnsureSuccessStatusCode();
        (await superadmin.PostAsJsonAsync($"/api/visitors/{visit.VisitId}/check-out", new { skipFeedback = true }))
            .EnsureSuccessStatusCode();

        var list = await superadmin.GetFromJsonAsync<ApiResponse<PagedResult<VisitorListItemDto>>>(
            "/api/visitors?quickFilter=manualcheckouttoday&pageSize=100&sortBy=checkout&sortDir=desc", JsonOptions);
        var row = Assert.Single(list!.Data!.Items, v => v.VisitId == visit.VisitId);
        Assert.True(row.IsManualCheckOut);
        Assert.All(list.Data.Items, v => Assert.True(v.IsManualCheckOut));

        var dashboard = await superadmin.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions);
        Assert.Equal(dashboard!.Data!.ManualCheckOutsToday, list.Data.TotalCount);
    }

    [Fact]
    public async Task Report_And_Visitor_List_Sort_Across_All_Rows()
    {
        var admin = await LoginAsync("superadmin");
        await DisableApprovalsAsync(admin);
        await RegisterAsync(admin, $"Aaa Sort {Guid.NewGuid():N}"[..24], recognizedVisitorId: null);
        await RegisterAsync(admin, $"Zzz Sort {Guid.NewGuid():N}"[..24], recognizedVisitorId: null);

        var today = AppTime.Today.ToString("yyyy-MM-dd");
        var report = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/visitors?format=json&reportType=range&dateFrom={today}&dateTo={today}&sortBy=visitor&sortDir=desc&page=1&pageSize=500",
            JsonOptions);
        var names = report.GetProperty("data").GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("visitorName").GetString()!).ToList();
        Assert.True(names.Count >= 2);
        Assert.Equal(names.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);

        var asc = await admin.GetFromJsonAsync<ApiResponse<PagedResult<VisitorListItemDto>>>(
            "/api/visitors?sortBy=visitor&sortDir=asc&pageSize=1", JsonOptions);
        var desc = await admin.GetFromJsonAsync<ApiResponse<PagedResult<VisitorListItemDto>>>(
            "/api/visitors?sortBy=visitor&sortDir=desc&pageSize=1", JsonOptions);
        Assert.True(string.Compare(asc!.Data!.Items[0].VisitorName, desc!.Data!.Items[0].VisitorName,
            StringComparison.OrdinalIgnoreCase) < 0);
    }

    private sealed record Registered(Guid VisitId, Guid VisitorId, string VisitorNumber, bool Inside);

    private async Task<Registered> RegisterAsync(HttpClient client, string name, Guid? recognizedVisitorId)
    {
        Guid departmentId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId && e.LocationId == null)
                .OrderBy(e => e.FullName).FirstAsync();
            departmentId = host.DepartmentId;
            hostId = host.Id;
            purposeId = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync();
        }

        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            gender = 1,
            companyName = "Entry Number Co",
            telephone = $"97{Random.Shared.Next(10000000, 99999999)}",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            photoBase64 = TestPhotos.Base64(name),
            recognizedVisitorId,
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        return new Registered(
            data.GetProperty("visitId").GetGuid(),
            data.GetProperty("visitorId").GetGuid(),
            data.GetProperty("visitorNumber").GetString()!,
            string.Equals(data.GetProperty("statusLabel").GetString(), "Inside", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DisableApprovalsAsync(HttpClient admin)
    {
        var settings = (await (await admin.GetAsync("/api/settings")).Content
            .ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        settings.ApprovalRequired = false;
        settings.WalkInApprovalRequired = false;
        (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
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
