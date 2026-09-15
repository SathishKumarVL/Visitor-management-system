using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Models.Product;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Negative tenant isolation + entitlement enforcement.
/// Proves Tenant A cannot obtain Tenant B data, and disabled modules fail closed on the backend.
/// </summary>
public class TenantIsolationTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TenantBAdmin = "tenantb-admin";
    private const string TenantBPassword = "TenantB_IsolationPass1!";

    public TenantIsolationTests(TestApiFactory factory) => _factory = factory;

    private async Task EnsureTenantBAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await db.Tenants.AnyAsync(t => t.Id == TenantBId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantBId,
                Code = "TENANTB",
                Name = "Tenant B Isolation",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var dept = await db.Departments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == TenantBId && d.Code == "ADMIN");
        if (dept is null)
        {
            dept = new Department
            {
                TenantId = TenantBId,
                Name = "Admin B",
                Code = "ADMIN",
                IsActive = true,
                CreatedBy = "test"
            };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
        }

        var host = await db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == TenantBId && e.FullName == "Host B");
        if (host is null)
        {
            host = new Employee
            {
                TenantId = TenantBId,
                FullName = "Host B",
                DepartmentId = dept.Id,
                IsActive = true,
                CreatedBy = "test"
            };
            db.Employees.Add(host);
            await db.SaveChangesAsync();
        }

        var purpose = await db.VisitPurposes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == TenantBId && p.Name == "Meeting");
        if (purpose is null)
        {
            purpose = new VisitPurpose { TenantId = TenantBId, Name = "Meeting", SortOrder = 1, IsActive = true, CreatedBy = "test" };
            db.VisitPurposes.Add(purpose);
            await db.SaveChangesAsync();
        }

        if (!await db.SystemSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == TenantBId && s.Key == "CompanyName"))
        {
            db.SystemSettings.Add(new SystemSetting
            {
                TenantId = TenantBId,
                Key = "CompanyName",
                Value = "Tenant B Corp",
                UpdatedBy = "test"
            });
            await db.SaveChangesAsync();
        }

        if (!await db.TenantLicenses.AnyAsync(l => l.TenantId == TenantBId))
        {
            db.TenantLicenses.Add(new TenantLicense
            {
                TenantId = TenantBId,
                Edition = "Enterprise",
                MaxSites = 10,
                MaxUsers = 50,
                IsActive = true,
                Status = "Active",
                GraceDaysAfterExpiry = 0
            });
        }

        foreach (var key in new[] { ModuleKeys.VisitorManagement, ModuleKeys.EmergencyManagement })
        {
            if (!await db.TenantModuleEntitlements.AnyAsync(m => m.TenantId == TenantBId && m.ModuleKey == key))
            {
                db.TenantModuleEntitlements.Add(new TenantModuleEntitlement
                {
                    TenantId = TenantBId,
                    ModuleKey = key,
                    IsEnabled = true
                });
            }
        }

        await db.SaveChangesAsync();

        var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == TenantBAdmin);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = TenantBAdmin,
                Email = "tenantb@example.test",
                EmailConfirmed = true,
                FullName = "Tenant B Admin",
                TenantId = TenantBId,
                DepartmentId = dept.Id,
                IsActive = true,
                MustChangePassword = false
            };
            var create = await users.CreateAsync(user, TenantBPassword);
            Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, AppRoles.Admin);
        }
        else
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, token, TenantBPassword);
            user.MustChangePassword = false;
            user.IsActive = true;
            user.TenantId = TenantBId;
            await users.UpdateAsync(user);
        }

        // Ensure Tenant B has at least one visit for IDOR tests
        if (!await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.TenantId == TenantBId))
        {
            var visitor = new Visitor
            {
                TenantId = TenantBId,
                VisitorNumber = "TB-SECRET-001",
                FullName = "Secret Visitor B",
                CompanyName = "OtherCorp",
                Email = "secretb@example.test",
                CreatedBy = "test"
            };
            db.Visitors.Add(visitor);
            await db.SaveChangesAsync();

            var visit = new VisitorVisit
            {
                TenantId = TenantBId,
                VisitorId = visitor.Id,
                VisitNumber = "TB-2026-000001",
                VisitDate = DateOnly.FromDateTime(DateTime.Today),
                VisitTime = TimeOnly.FromDateTime(DateTime.Now),
                DepartmentId = dept.Id,
                HostEmployeeId = host.Id,
                Status = VisitStatus.Inside,
                NumberOfPersons = 1,
                CreatedBy = "test"
            };
            db.VisitorVisits.Add(visit);
            await db.SaveChangesAsync();

            db.VisitorVisitPurposes.Add(new VisitorVisitPurpose
            {
                VisitorVisitId = visit.Id,
                VisitPurposeId = purpose.Id
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task<(HttpClient Client, string Token)> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password,
            rememberMe = false
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }

    private async Task EnsureReceptionAsync()
    {
        await _factory.EnsureReceptionPasswordAsync();
    }

    [Fact]
    public async Task TenantA_Cannot_List_TenantB_Visitors_In_Search()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);

        var response = await client.GetAsync("/api/visitors?page=1&pageSize=100&search=Secret%20Visitor%20B");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = json.GetProperty("data").GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            Assert.DoesNotContain("Secret Visitor B", item.GetProperty("visitorName").GetString());
            Assert.DoesNotContain("TB-", item.GetProperty("visitNumber").GetString() ?? "");
        }
    }

    [Fact]
    public async Task TenantA_Cannot_Get_TenantB_Visit_By_Id()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();

        Guid visitId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            visitId = await db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == TenantBId)
                .Select(v => v.Id)
                .FirstAsync();
        }

        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);
        var response = await client.GetAsync($"/api/visitors/{visitId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantA_Cannot_See_TenantB_Company_In_Settings()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);

        var response = await client.GetAsync("/api/settings");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var company = json.GetProperty("data").GetProperty("companyName").GetString();
        Assert.NotEqual("Tenant B Corp", company);
    }

    [Fact]
    public async Task TenantA_Cannot_Access_TenantB_Media_File()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();

        // Plant a file only under Tenant B's private root
        string fileName;
        using (var scope = _factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(TenantBId);
            var media = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
            var jpeg = Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGfAP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Bf//Z");
            // Minimal valid JPEG may fail magic-byte check — use a real tiny JPEG:
            jpeg = CreateMinimalJpeg();
            fileName = await media.SaveVisitorPhotoAsync(Guid.NewGuid(), jpeg);
        }

        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);
        var response = await client.GetAsync($"/api/media/{fileName}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Path traversal attempts
        var traversal = await client.GetAsync("/api/media/../" + fileName);
        Assert.True(traversal.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TenantA_Report_Does_Not_Contain_TenantB_Visits()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);

        var response = await client.GetAsync(
            $"/api/reports/visitors?reportType=daterange&dateFrom={DateTime.Today.AddDays(-7):yyyy-MM-dd}&dateTo={DateTime.Today:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret Visitor B", body);
        Assert.DoesNotContain("TB-2026-000001", body);
        Assert.DoesNotContain("OtherCorp", body);
    }

    [Fact]
    public async Task TenantA_Csv_Export_Does_Not_Contain_TenantB()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);

        var response = await client.GetAsync(
            $"/api/reports/visitors?reportType=daterange&format=csv&dateFrom={DateTime.Today.AddDays(-30):yyyy-MM-dd}&dateTo={DateTime.Today:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret Visitor B", body);
        Assert.DoesNotContain("TB-2026-", body);
    }

    [Fact]
    public async Task TenantA_Cannot_Get_TenantB_Pass()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        Guid visitId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            visitId = await db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == TenantBId).Select(v => v.Id).FirstAsync();
        }

        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);
        var response = await client.GetAsync($"/api/pass/{visitId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantA_Masters_Do_Not_Include_TenantB_Departments()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);

        var response = await client.GetAsync("/api/masters/departments?activeOnly=false");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        foreach (var item in json.GetProperty("data").EnumerateArray())
            Assert.NotEqual("Admin B", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Analytics_Module_Disabled_Returns_403()
    {
        await EnsureReceptionAsync();
        // Seed TIAANO admin for analytics role check
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == "admin");
            if (admin is not null)
            {
                var token = await users.GeneratePasswordResetTokenAsync(admin);
                await users.ResetPasswordAsync(admin, token, TestSecrets.SeedPassword);
                admin.MustChangePassword = false;
                await users.UpdateAsync(admin);
            }
        }

        var (client, _) = await LoginAsync("admin", TestSecrets.SeedPassword);
        var response = await client.GetAsync("/api/analytics/summary");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Emergency_Disabled_Tenant_Returns_403()
    {
        await EnsureTenantBAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.TenantModuleEntitlements
                .FirstAsync(m => m.TenantId == TenantBId && m.ModuleKey == ModuleKeys.EmergencyManagement);
            row.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        try
        {
            var (client, _) = await LoginAsync(TenantBAdmin, TenantBPassword);
            var response = await client.GetAsync("/api/emergency/inside");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            // Visitor management still works
            var visitors = await client.GetAsync("/api/visitors/inside");
            visitors.EnsureSuccessStatusCode();
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.TenantModuleEntitlements
                .FirstAsync(m => m.TenantId == TenantBId && m.ModuleKey == ModuleKeys.EmergencyManagement);
            row.IsEnabled = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task VisitorManagement_Disabled_Rejects_Visitor_Api()
    {
        await EnsureTenantBAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.TenantModuleEntitlements
                .FirstAsync(m => m.TenantId == TenantBId && m.ModuleKey == ModuleKeys.VisitorManagement);
            row.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        try
        {
            var (client, _) = await LoginAsync(TenantBAdmin, TenantBPassword);
            var response = await client.GetAsync("/api/visitors/inside");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.TenantModuleEntitlements
                .FirstAsync(m => m.TenantId == TenantBId && m.ModuleKey == ModuleKeys.VisitorManagement);
            row.IsEnabled = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Expired_License_Rejects_Module_Access()
    {
        await EnsureTenantBAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var license = await db.TenantLicenses.FirstAsync(l => l.TenantId == TenantBId);
            license.ExpiresAt = DateTime.UtcNow.AddDays(-2);
            license.GraceDaysAfterExpiry = 0;
            await db.SaveChangesAsync();
        }

        try
        {
            var (client, _) = await LoginAsync(TenantBAdmin, TenantBPassword);
            var response = await client.GetAsync("/api/visitors/inside");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var license = await db.TenantLicenses.FirstAsync(l => l.TenantId == TenantBId);
            license.ExpiresAt = null;
            license.GraceDaysAfterExpiry = 0;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TenantB_Can_See_Own_Visitor()
    {
        await EnsureTenantBAsync();
        Guid visitId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            visitId = await db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == TenantBId).Select(v => v.Id).FirstAsync();
        }

        var (client, _) = await LoginAsync(TenantBAdmin, TenantBPassword);
        var response = await client.GetAsync($"/api/visitors/{visitId}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Secret Visitor B", json.GetProperty("data").GetProperty("visitorName").GetString());
    }

    [Fact]
    public async Task Forged_Tenant_Header_Does_Not_Switch_Tenant()
    {
        await EnsureTenantBAsync();
        await EnsureReceptionAsync();
        var (client, _) = await LoginAsync("reception", TestSecrets.SeedPassword);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantBId.ToString());

        var response = await client.GetAsync("/api/settings");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.NotEqual("Tenant B Corp", json.GetProperty("data").GetProperty("companyName").GetString());
    }

    private static byte[] CreateMinimalJpeg()
    {
        // 1x1 JPEG
        return Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==");
    }
}
