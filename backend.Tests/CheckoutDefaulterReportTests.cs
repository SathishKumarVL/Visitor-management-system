using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class CheckoutDefaulterReportTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CheckoutDefaulterReportTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Defaulter_Report_Lists_Overdue_Forced_And_Late_Visits_Only()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var now = DateTime.UtcNow;
        await SeedVisitAsync($"Overdue {tag}", VisitStatus.Inside, now.AddDays(-2), null, manual: false);
        await SeedVisitAsync($"RecentInside {tag}", VisitStatus.Inside, now.AddMinutes(-5), null, manual: false);
        await SeedVisitAsync($"Forced {tag}", VisitStatus.CheckedOut, now.AddMinutes(-60), now.AddMinutes(-30), manual: true);
        await SeedVisitAsync($"Late {tag}", VisitStatus.CheckedOut, now.AddDays(-2), now.AddDays(-1), manual: false);
        await SeedVisitAsync($"OnTime {tag}", VisitStatus.CheckedOut, now.AddMinutes(-60), now.AddMinutes(-30), manual: false);

        var client = await LoginAsync("superadmin");
        var url = $"/api/reports/visitors?format=json&reportType=defaulters" +
                  $"&dateFrom={DateTime.Today.AddDays(-1):yyyy-MM-dd}&dateTo={DateTime.Today.AddDays(1):yyyy-MM-dd}";
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        var rows = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("rows")
            .EnumerateArray()
            .Where(r => r.GetProperty("visitorName").GetString()!.EndsWith(tag))
            .ToDictionary(r => r.GetProperty("visitorName").GetString()!, r => r.GetProperty("defaulterReason").GetString()!);

        Assert.Equal(3, rows.Count);
        Assert.StartsWith("Not checked out", rows[$"Overdue {tag}"]);
        Assert.Contains("manually", rows[$"Forced {tag}"]);
        Assert.StartsWith("Late check-out", rows[$"Late {tag}"]);
    }

    [Fact]
    public async Task Defaulter_Report_Exports_Excel()
    {
        var client = await LoginAsync("superadmin");
        var response = await client.GetAsync(
            $"/api/reports/visitors?format=excel&reportType=defaulters&dateFrom={DateTime.Today.AddDays(-7):yyyy-MM-dd}&dateTo={DateTime.Today:yyyy-MM-dd}");
        Assert.True(response.IsSuccessStatusCode);
        using var wb = new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.True(wb.Worksheets.Contains("Defaulters"));
    }

    [Fact]
    public async Task Report_Json_Pages_Rows_But_Totals_Cover_Everything()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var now = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            await SeedVisitAsync($"Paged{i} {tag}", VisitStatus.CheckedOut, now.AddMinutes(-60), now.AddMinutes(-30), manual: false);

        var client = await LoginAsync("superadmin");
        var baseUrl = $"/api/reports/visitors?format=json&reportType=range&company=Defaulter%20Co" +
                      $"&dateFrom={DateTime.Today.AddDays(-1):yyyy-MM-dd}&dateTo={DateTime.Today.AddDays(1):yyyy-MM-dd}";

        var all = JsonDocument.Parse(await client.GetStringAsync(baseUrl)).RootElement.GetProperty("data");
        var total = all.GetProperty("total").GetInt32();
        Assert.True(total >= 3);
        Assert.Equal(total, all.GetProperty("rows").GetArrayLength());

        var page2 = JsonDocument.Parse(await client.GetStringAsync($"{baseUrl}&page=2&pageSize=2")).RootElement.GetProperty("data");
        Assert.Equal(total, page2.GetProperty("total").GetInt32());
        Assert.Equal(2, page2.GetProperty("page").GetInt32());
        Assert.Equal(2, page2.GetProperty("pageSize").GetInt32());
        Assert.Equal(Math.Min(2, total - 2), page2.GetProperty("rows").GetArrayLength());

        var expectedNumbers = all.GetProperty("rows").EnumerateArray().Skip(2).Take(2)
            .Select(r => r.GetProperty("visitNumber").GetString()).ToList();
        var pageNumbers = page2.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("visitNumber").GetString()).ToList();
        Assert.Equal(expectedNumbers, pageNumbers);
    }

    [Fact]
    public async Task Feedback_Responses_Paged_Endpoint_Returns_Paged_Result()
    {
        var client = await LoginAsync("superadmin");
        var response = await client.GetAsync("/api/masters/feedback-responses/paged?page=1&pageSize=5");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(5, data.GetProperty("pageSize").GetInt32());
        Assert.True(data.GetProperty("items").GetArrayLength() <= 5);
        Assert.True(data.GetProperty("totalCount").GetInt32() >= data.GetProperty("items").GetArrayLength());
    }

    private async Task SeedVisitAsync(string visitorName, VisitStatus status, DateTime checkIn, DateTime? checkOut, bool manual)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "admin");
        var tenantId = admin.TenantId;
        var host = await db.Employees.IgnoreQueryFilters().FirstAsync(e => e.TenantId == tenantId && e.IsActive);

        var visitor = new Visitor
        {
            TenantId = tenantId,
            VisitorNumber = $"DF-{Guid.NewGuid():N}"[..20],
            FullName = visitorName,
            CompanyName = "Defaulter Co",
            Phone = "9876500000",
            CreatedBy = "test"
        };
        db.Visitors.Add(visitor);
        await db.SaveChangesAsync();

        db.VisitorVisits.Add(new VisitorVisit
        {
            TenantId = tenantId,
            VisitorId = visitor.Id,
            VisitNumber = $"DF-{Guid.NewGuid():N}"[..20],
            VisitDate = DateOnly.FromDateTime(DateTime.Today),
            VisitTime = TimeOnly.FromDateTime(DateTime.Now),
            DepartmentId = host.DepartmentId,
            HostEmployeeId = host.Id,
            Status = status,
            CheckInAt = checkIn,
            CheckOutAt = checkOut,
            IsManualCheckOut = manual,
            NumberOfPersons = 1,
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
