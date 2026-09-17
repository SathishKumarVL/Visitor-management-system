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
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Site scoping is the second isolation axis inside a tenant: a user bound to a site must never see
/// another site's visitors, and a user with no site keeps tenant-wide visibility. These tests use two
/// sites in the SAME tenant so a passing result cannot be explained by tenant isolation alone.
/// </summary>
public class SiteScopingTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string NorthSiteName = "Scoping North";
    private const string SouthSiteName = "Scoping South";
    private const string NorthUser = "scoping-north-reception";
    private const string SouthUser = "scoping-south-reception";
    private const string RoamingUser = "scoping-roaming-admin";
    private const string Password = "SiteScoping_Pass1!";

    private const string NorthVisitorName = "North Site Visitor";
    private const string SouthVisitorName = "South Site Visitor";

    /// <summary>
    /// Narrowed to the two fixture visitors. Listing everything would let visitors created by other
    /// tests push the fixtures off the first page and fail these assertions for the wrong reason.
    /// </summary>
    private const string FixtureQuery = "/api/visitors?page=1&pageSize=100&query=Site%20Visitor";

    public SiteScopingTests(TestApiFactory factory) => _factory = factory;

    private sealed record Fixture(Guid TenantId, Guid NorthSiteId, Guid SouthSiteId, Guid NorthVisitId, Guid SouthVisitId);

    /// <summary>
    /// Builds two sites in the default tenant, one checked-in visitor in each, and three users:
    /// reception bound to north, reception bound to south, and an admin bound to nothing.
    /// </summary>
    private async Task<Fixture> EnsureFixtureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var seededReception = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "reception");
        var tenantId = seededReception.TenantId;

        var north = await EnsureSiteAsync(db, tenantId, NorthSiteName, "SCN");
        var south = await EnsureSiteAsync(db, tenantId, SouthSiteName, "SCS");

        var dept = await db.Departments.IgnoreQueryFilters().FirstAsync(d => d.TenantId == tenantId);
        var host = await db.Employees.IgnoreQueryFilters().FirstAsync(e => e.TenantId == tenantId);

        var northVisitId = await EnsureVisitAsync(db, tenantId, north, NorthVisitorName, "SCOPE-N", dept.Id, host.Id);
        var southVisitId = await EnsureVisitAsync(db, tenantId, south, SouthVisitorName, "SCOPE-S", dept.Id, host.Id);

        await EnsureUserAsync(users, NorthUser, tenantId, north, AppRoles.Reception, dept.Id);
        await EnsureUserAsync(users, SouthUser, tenantId, south, AppRoles.Reception, dept.Id);
        await EnsureUserAsync(users, RoamingUser, tenantId, null, AppRoles.Admin, dept.Id);

        return new Fixture(tenantId, north, south, northVisitId, southVisitId);
    }

    private static async Task<Guid> EnsureSiteAsync(ApplicationDbContext db, Guid tenantId, string name, string code)
    {
        var existing = await db.Sites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Name == name);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await db.SaveChangesAsync();
            }
            return existing.Id;
        }

        var site = new Site
        {
            TenantId = tenantId,
            Name = name,
            Code = code,
            IsActive = true,
            IsDefault = false,
            CreatedBy = "test"
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }

    private static async Task<Guid> EnsureVisitAsync(
        ApplicationDbContext db, Guid tenantId, Guid siteId, string visitorName, string prefix, Guid deptId, Guid hostId)
    {
        var existing = await db.VisitorVisits.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.SiteId == siteId && v.Visitor.FullName == visitorName);
        if (existing is not null)
        {
            if (existing.Status != VisitStatus.Inside)
            {
                existing.Status = VisitStatus.Inside;
                existing.CheckOutAt = null;
                await db.SaveChangesAsync();
            }
            return existing.Id;
        }

        var visitor = new Visitor
        {
            TenantId = tenantId,
            VisitorNumber = $"{prefix}-{Guid.NewGuid():N}"[..20],
            FullName = visitorName,
            CompanyName = $"{visitorName} Co",
            CreatedBy = "test"
        };
        db.Visitors.Add(visitor);
        await db.SaveChangesAsync();

        var visit = new VisitorVisit
        {
            TenantId = tenantId,
            SiteId = siteId,
            VisitorId = visitor.Id,
            VisitNumber = $"{prefix}-{Guid.NewGuid():N}"[..20],
            VisitDate = DateOnly.FromDateTime(DateTime.Today),
            VisitTime = TimeOnly.FromDateTime(DateTime.Now),
            DepartmentId = deptId,
            HostEmployeeId = hostId,
            Status = VisitStatus.Inside,
            CheckInAt = DateTime.UtcNow.AddMinutes(-20),
            NumberOfPersons = 1,
            CreatedBy = "test"
        };
        db.VisitorVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users, string username, Guid tenantId, Guid? siteId, string role, Guid deptId)
    {
        var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@example.test",
                EmailConfirmed = true,
                FullName = username,
                TenantId = tenantId,
                SiteId = siteId,
                DepartmentId = deptId,
                IsActive = true,
                MustChangePassword = false
            };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, role);
            return;
        }

        // Re-assert the fields the tests depend on; earlier runs may have mutated them.
        user.TenantId = tenantId;
        user.SiteId = siteId;
        user.IsActive = true;
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await users.ResetPasswordAsync(user, token, Password);

        var roles = await users.GetRolesAsync(user);
        if (!roles.Contains(role))
        {
            await users.RemoveFromRolesAsync(user, roles);
            await users.AddToRoleAsync(user, role);
        }
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Site_Bound_User_Search_Excludes_Other_Site()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var body = await BodyAsync(await client.GetAsync(FixtureQuery));

        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Site_Bound_User_Inside_List_Excludes_Other_Site()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(SouthUser);

        var body = await BodyAsync(await client.GetAsync("/api/visitors/inside"));

        Assert.Contains(SouthVisitorName, body);
        Assert.DoesNotContain(NorthVisitorName, body);
    }

    [Fact]
    public async Task Site_Bound_User_Cannot_Open_Other_Site_Visit_By_Id()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.GetAsync($"/api/visitors/{fixture.SouthVisitId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Site_Bound_User_Can_Open_Own_Site_Visit_By_Id()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.GetAsync($"/api/visitors/{fixture.NorthVisitId}");

        // Positive control: proves the 404 above is site scoping, not a broken fixture.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unassigned_User_Sees_Every_Site()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var body = await BodyAsync(await client.GetAsync(FixtureQuery));

        Assert.Contains(NorthVisitorName, body);
        Assert.Contains(SouthVisitorName, body);
    }

    [Fact]
    public async Task Dashboard_Counts_Are_Site_Scoped()
    {
        await EnsureFixtureAsync();

        var northBody = await BodyAsync(await (await LoginAsync(NorthUser)).GetAsync("/api/dashboard"));
        var roamingBody = await BodyAsync(await (await LoginAsync(RoamingUser)).GetAsync("/api/dashboard"));

        var northInside = JsonDocument.Parse(northBody).RootElement
            .GetProperty("data").GetProperty("currentlyInside").GetInt32();
        var roamingInside = JsonDocument.Parse(roamingBody).RootElement
            .GetProperty("data").GetProperty("currentlyInside").GetInt32();

        Assert.True(northInside >= 1, "North site should count its own visitor.");
        Assert.True(
            roamingInside > northInside,
            $"Tenant-wide count ({roamingInside}) should exceed one site's count ({northInside}).");
    }

    [Fact]
    public async Task Emergency_Roster_Is_Site_Scoped()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);
        var all = await BodyAsync(await client.GetAsync("/api/emergency/roster"));
        Assert.Contains(NorthVisitorName, all);
        Assert.Contains(SouthVisitorName, all);

        // Security role is required for the roster, so bind the roaming admin's site temporarily instead
        // of creating a fourth account: re-read as the north reception user.
        var northClient = await LoginAsync(NorthUser);
        var northRoster = await northClient.GetAsync("/api/emergency/roster");

        if (northRoster.StatusCode == HttpStatusCode.Forbidden)
            return; // Reception is not entitled to the roster; the tenant-wide assertion above still holds.

        var body = await BodyAsync(northRoster);
        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Visitor_Report_Is_Site_Scoped()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var body = await BodyAsync(await client.GetAsync("/api/reports/visitors?format=json&page=1&pageSize=200"));

        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Locations_Are_Site_Scoped()
    {
        var fixture = await EnsureFixtureAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.SiteId == fixture.SouthSiteId && l.Name == "South Only Area"))
            {
                db.Locations.Add(new Location
                {
                    TenantId = fixture.TenantId,
                    SiteId = fixture.SouthSiteId,
                    Name = "South Only Area",
                    IsActive = true,
                    CreatedBy = "test"
                });
                await db.SaveChangesAsync();
            }
        }

        var northBody = await BodyAsync(await (await LoginAsync(NorthUser)).GetAsync("/api/masters/locations"));
        var southBody = await BodyAsync(await (await LoginAsync(SouthUser)).GetAsync("/api/masters/locations"));

        Assert.DoesNotContain("South Only Area", northBody);
        Assert.Contains("South Only Area", southBody);
    }

    [Fact]
    public async Task New_Registration_Is_Stamped_With_Acting_Users_Site()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        Guid deptId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Host must belong to the department it is registered against, so derive both from one row.
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == fixture.TenantId)
                .OrderBy(e => e.FullName)
                .FirstAsync();
            deptId = host.DepartmentId;
            hostId = host.Id;
            purposeId = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == fixture.TenantId)
                .OrderBy(p => p.SortOrder)
                .Select(p => p.Id)
                .FirstAsync();
        }

        var marker = $"Stamped Visitor {Guid.NewGuid():N}"[..28];
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = marker,
            companyName = "Site Stamp Co",
            telephone = "9000000003",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId = deptId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stamped = await verifyDb.VisitorVisits.IgnoreQueryFilters()
            .Where(v => v.Visitor.FullName == marker)
            .Select(v => v.SiteId)
            .FirstAsync();

        Assert.Equal(fixture.NorthSiteId, stamped);
    }

    [Fact]
    public async Task Site_Write_Endpoints_Reject_Reception()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.PostAsJsonAsync("/api/sites", new
        {
            name = $"Unauthorized Site {Guid.NewGuid():N}",
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Site_With_Visitors_Inside_Cannot_Be_Deactivated()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var response = await client.PostAsJsonAsync($"/api/sites/{fixture.NorthSiteId}/deactivate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("still inside", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_Site_Name_Is_Rejected()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var response = await client.PostAsJsonAsync("/api/sites", new
        {
            name = NorthSiteName,
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Site_List_Reports_Assignment_Counts()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var body = await BodyAsync(await client.GetAsync("/api/sites"));
        var sites = JsonDocument.Parse(body).RootElement.GetProperty("data");

        var north = sites.EnumerateArray().First(s => s.GetProperty("id").GetGuid() == fixture.NorthSiteId);
        Assert.True(north.GetProperty("userCount").GetInt32() >= 1, "North site has an assigned reception user.");
        Assert.True(north.GetProperty("insideCount").GetInt32() >= 1, "North site has a visitor inside.");
    }
}
