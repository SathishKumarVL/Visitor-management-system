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
/// Location scoping is the second isolation axis inside a tenant: a user bound to a location must never
/// see visits that do not include that location, while a user with no location keeps tenant-wide visibility.
/// </summary>
public class LocationScopingTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string NorthLocationName = "Scoping North";
    private const string SouthLocationName = "Scoping South";
    private const string NorthUser = "scoping-north-reception";
    private const string SouthUser = "scoping-south-reception";
    private const string RoamingUser = "scoping-roaming-admin";
    private const string UnassignedAdmin = "scoping-unassigned-admin";
    private const string Password = "LocationScoping_Pass1!";

    private const string NorthVisitorName = "North Location Visitor";
    private const string SouthVisitorName = "South Location Visitor";

    /// <summary>
    /// Narrowed to the two fixture visitors so siblings created by other tests cannot push them off page 1.
    /// </summary>
    private const string FixtureQuery = "/api/visitors?page=1&pageSize=100&query=Location%20Visitor";

    public LocationScopingTests(TestApiFactory factory) => _factory = factory;

    private sealed record Fixture(
        Guid TenantId,
        Guid NorthLocationId,
        Guid SouthLocationId,
        Guid NorthVisitId,
        Guid SouthVisitId);

    private async Task<Fixture> EnsureFixtureAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var seededReception = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "reception");
        var tenantId = seededReception.TenantId;

        var north = await EnsureLocationAsync(db, tenantId, NorthLocationName);
        var south = await EnsureLocationAsync(db, tenantId, SouthLocationName);

        var dept = await db.Departments.IgnoreQueryFilters().FirstAsync(d => d.TenantId == tenantId);
        var host = await db.Employees.IgnoreQueryFilters().FirstAsync(e => e.TenantId == tenantId);

        var northVisitId = await EnsureVisitAsync(db, tenantId, north, NorthVisitorName, "SCOPE-N", dept.Id, host.Id);
        var southVisitId = await EnsureVisitAsync(db, tenantId, south, SouthVisitorName, "SCOPE-S", dept.Id, host.Id);

        await EnsureUserAsync(users, NorthUser, tenantId, north, AppRoles.Reception, dept.Id);
        await EnsureUserAsync(users, SouthUser, tenantId, south, AppRoles.Reception, dept.Id);
        await EnsureUserAsync(users, RoamingUser, tenantId, null, AppRoles.SuperAdmin, dept.Id);
        await EnsureUserAsync(users, UnassignedAdmin, tenantId, null, AppRoles.Admin, dept.Id);

        return new Fixture(tenantId, north, south, northVisitId, southVisitId);
    }

    private static async Task<Guid> EnsureLocationAsync(ApplicationDbContext db, Guid tenantId, string name)
    {
        var existing = await db.Locations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Name == name);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await db.SaveChangesAsync();
            }
            return existing.Id;
        }

        var location = new Location
        {
            TenantId = tenantId,
            Name = name,
            IsActive = true,
            CreatedBy = "test"
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private static async Task<Guid> EnsureVisitAsync(
        ApplicationDbContext db, Guid tenantId, Guid locationId, string visitorName, string prefix, Guid deptId, Guid hostId)
    {
        var existing = await db.VisitorVisits.IgnoreQueryFilters()
            .Include(v => v.VisitLocations)
            .Include(v => v.Visitor)
            .FirstOrDefaultAsync(v =>
                v.TenantId == tenantId
                && v.Visitor.FullName == visitorName
                && v.VisitLocations.Any(vl => vl.LocationId == locationId));
        if (existing is not null)
        {
            // The dashboard only counts recent visits, so a fixture reused across days must look current.
            var today = Tiaano.Vms.Api.Services.AppTime.Today;
            if (existing.Status != VisitStatus.Inside || existing.VisitDate != today)
            {
                existing.Status = VisitStatus.Inside;
                existing.CheckOutAt = null;
                existing.VisitDate = today;
                existing.CheckInAt = DateTime.UtcNow.AddMinutes(-20);
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
            VisitorId = visitor.Id,
            VisitNumber = $"{prefix}-{Guid.NewGuid():N}"[..20],
            VisitDate = Tiaano.Vms.Api.Services.AppTime.Today,
            VisitTime = TimeOnly.FromDateTime(DateTime.Now),
            DepartmentId = deptId,
            HostEmployeeId = hostId,
            Status = VisitStatus.Inside,
            CheckInAt = DateTime.UtcNow.AddMinutes(-20),
            NumberOfPersons = 1,
            CreatedBy = "test"
        };
        visit.VisitLocations.Add(new VisitorVisitLocation { LocationId = locationId });
        db.VisitorVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users, string username, Guid tenantId, Guid? locationId, string role, Guid deptId)
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
                LocationId = locationId,
                DepartmentId = deptId,
                IsActive = true,
                MustChangePassword = false
            };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, role);
            return;
        }

        user.TenantId = tenantId;
        user.LocationId = locationId;
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
    public async Task Location_Bound_User_Search_Excludes_Other_Location()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var body = await BodyAsync(await client.GetAsync(FixtureQuery));

        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Location_Bound_User_Inside_List_Excludes_Other_Location()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(SouthUser);

        var body = await BodyAsync(await client.GetAsync("/api/visitors/inside"));

        Assert.Contains(SouthVisitorName, body);
        Assert.DoesNotContain(NorthVisitorName, body);
    }

    [Fact]
    public async Task Location_Bound_User_Cannot_Open_Other_Location_Visit_By_Id()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.GetAsync($"/api/visitors/{fixture.SouthVisitId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Location_Bound_User_Can_Open_Own_Location_Visit_By_Id()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.GetAsync($"/api/visitors/{fixture.NorthVisitId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_Sees_Every_Location()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var body = await BodyAsync(await client.GetAsync(FixtureQuery));

        Assert.Contains(NorthVisitorName, body);
        Assert.Contains(SouthVisitorName, body);
    }

    [Fact]
    public async Task SuperAdmin_With_A_Location_Still_Sees_Every_Location()
    {
        var fixture = await EnsureFixtureAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await EnsureUserAsync(users, RoamingUser, fixture.TenantId, fixture.NorthLocationId, AppRoles.SuperAdmin,
                (await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == RoamingUser)).DepartmentId!.Value);
        }
        try
        {
            var body = await BodyAsync(await (await LoginAsync(RoamingUser)).GetAsync(FixtureQuery));
            Assert.Contains(NorthVisitorName, body);
            Assert.Contains(SouthVisitorName, body);
        }
        finally
        {
            await EnsureFixtureAsync();
        }
    }

    [Fact]
    public async Task Admin_Without_A_Location_Cannot_Sign_In()
    {
        await EnsureFixtureAsync();
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = UnassignedAdmin, password = Password, rememberMe = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no active location", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Location_Bound_Admin_Only_Manages_Users_At_Their_Location()
    {
        var fixture = await EnsureFixtureAsync();
        const string northAdmin = "scoping-north-admin";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var deptId = (await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == NorthUser)).DepartmentId!.Value;
            await EnsureUserAsync(users, northAdmin, fixture.TenantId, fixture.NorthLocationId, AppRoles.Admin, deptId);
        }
        var admin = await LoginAsync(northAdmin);

        var list = await BodyAsync(await admin.GetAsync("/api/users"));
        Assert.Contains(NorthUser, list);
        Assert.DoesNotContain(SouthUser, list);
        Assert.DoesNotContain(RoamingUser, list);

        var southEmployee = await TestUsers.CreateEmployeeAsync(_factory.Services, fixture.TenantId, fixture.SouthLocationId);
        var elsewhere = await admin.PostAsJsonAsync($"/api/users/from-employee/{southEmployee}", new
        {
            username = $"wrong{Guid.NewGuid():N}"[..18],
            role = AppRoles.Reception,
            password = Password
        });
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);

        var northEmployee = await TestUsers.CreateEmployeeAsync(_factory.Services, fixture.TenantId, fixture.NorthLocationId);
        var superAdmin = await admin.PostAsJsonAsync($"/api/users/from-employee/{northEmployee}", new
        {
            username = $"esc{Guid.NewGuid():N}"[..18],
            role = AppRoles.SuperAdmin,
            password = Password
        });
        Assert.Equal(HttpStatusCode.Forbidden, superAdmin.StatusCode);
    }

    [Fact]
    public async Task Dashboard_Counts_Are_Location_Scoped()
    {
        await EnsureFixtureAsync();

        var northBody = await BodyAsync(await (await LoginAsync(NorthUser)).GetAsync("/api/dashboard"));
        var roamingBody = await BodyAsync(await (await LoginAsync(RoamingUser)).GetAsync("/api/dashboard"));

        var northInside = JsonDocument.Parse(northBody).RootElement
            .GetProperty("data").GetProperty("currentlyInside").GetInt32();
        var roamingInside = JsonDocument.Parse(roamingBody).RootElement
            .GetProperty("data").GetProperty("currentlyInside").GetInt32();

        Assert.True(northInside >= 1, "North location should count its own visitor.");
        Assert.True(
            roamingInside > northInside,
            $"Tenant-wide count ({roamingInside}) should exceed one location's count ({northInside}).");
    }

    [Fact]
    public async Task Emergency_Roster_Is_Location_Scoped()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);
        var all = await BodyAsync(await client.GetAsync("/api/emergency/roster"));
        Assert.Contains(NorthVisitorName, all);
        Assert.Contains(SouthVisitorName, all);

        var northClient = await LoginAsync(NorthUser);
        var northRoster = await northClient.GetAsync("/api/emergency/roster");

        if (northRoster.StatusCode == HttpStatusCode.Forbidden)
            return;

        var body = await BodyAsync(northRoster);
        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Visitor_Report_Is_Location_Scoped_For_Bound_User()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var body = await BodyAsync(await client.GetAsync("/api/reports/visitors?format=json&page=1&pageSize=200"));

        Assert.Contains(NorthVisitorName, body);
        Assert.DoesNotContain(SouthVisitorName, body);
    }

    [Fact]
    public async Task Visitor_Report_LocationId_Filter_Returns_Only_Matching_Visits()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(RoamingUser);

        var body = await BodyAsync(await client.GetAsync(
            $"/api/reports/visitors?format=json&page=1&pageSize=200&locationId={fixture.SouthLocationId}"));

        Assert.Contains(SouthVisitorName, body);
        Assert.DoesNotContain(NorthVisitorName, body);
    }

    [Fact]
    public async Task New_Registration_Includes_Acting_Users_Location()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        Guid deptId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == fixture.TenantId
                    && (e.LocationId == null || e.LocationId == fixture.NorthLocationId))
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
            gender = 1,
            companyName = "Location Stamp Co",
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
        var stampedLocations = await verifyDb.VisitorVisits.IgnoreQueryFilters()
            .Where(v => v.Visitor.FullName == marker)
            .SelectMany(v => v.VisitLocations.Select(vl => vl.LocationId))
            .ToListAsync();

        Assert.Contains(fixture.NorthLocationId, stampedLocations);
    }

    [Fact]
    public async Task Booking_By_Location_Bound_User_Appears_In_Their_Expected_List()
    {
        var fixture = await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        Guid deptId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == fixture.TenantId && e.LocationId == null)
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

        var date = Tiaano.Vms.Api.Services.AppTime.Today.AddDays(1);
        var marker = $"Booked North {Guid.NewGuid():N}"[..26];
        var response = await client.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = marker,
            gender = 1,
            companyName = "Booking Scope Co",
            phone = $"97{Random.Shared.Next(10000000, 99999999)}",
            expectedDate = date.ToString("yyyy-MM-dd"),
            expectedTime = "10:30:00",
            departmentId = deptId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var list = await BodyAsync(await client.GetAsync($"/api/visitors/expected?date={date:yyyy-MM-dd}"));
        Assert.Contains(marker, list);
    }

    private const string NorthEmployeeName = "Zz Scoping North Employee";
    private const string SouthEmployeeName = "Zz Scoping South Employee";

    private async Task<(Guid DepartmentId, Guid SouthEmployeeId)> EnsureScopedEmployeesAsync(Fixture fixture)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deptId = await db.Departments.IgnoreQueryFilters()
            .Where(d => d.TenantId == fixture.TenantId && d.IsActive && d.LocationId == null)
            .OrderBy(d => d.Name).Select(d => d.Id).FirstAsync();

        async Task<Guid> EnsureAsync(string name, Guid locationId)
        {
            var existing = await db.Employees.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.TenantId == fixture.TenantId && e.FullName == name);
            if (existing is not null)
            {
                existing.LocationId = locationId;
                existing.DepartmentId = deptId;
                existing.IsActive = true;
                await db.SaveChangesAsync();
                return existing.Id;
            }
            var employee = new Employee
            {
                TenantId = fixture.TenantId,
                FullName = name,
                DepartmentId = deptId,
                LocationId = locationId,
                IsActive = true,
                CreatedBy = "test"
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            return employee.Id;
        }

        await EnsureAsync(NorthEmployeeName, fixture.NorthLocationId);
        var southId = await EnsureAsync(SouthEmployeeName, fixture.SouthLocationId);
        return (deptId, southId);
    }

    [Fact]
    public async Task Employees_Are_Scoped_To_The_Users_Location()
    {
        var fixture = await EnsureFixtureAsync();
        await EnsureScopedEmployeesAsync(fixture);

        var north = await LoginAsync(NorthUser);
        var own = await BodyAsync(await north.GetAsync("/api/masters/hosts?activeOnly=false"));
        Assert.Contains(NorthEmployeeName, own);
        Assert.DoesNotContain(SouthEmployeeName, own);

        var asked = await BodyAsync(await north.GetAsync(
            $"/api/masters/hosts?activeOnly=false&locationId={fixture.SouthLocationId}"));
        Assert.DoesNotContain(SouthEmployeeName, asked);

        var roaming = await BodyAsync(await (await LoginAsync(RoamingUser)).GetAsync("/api/masters/hosts?activeOnly=false"));
        Assert.Contains(NorthEmployeeName, roaming);
        Assert.Contains(SouthEmployeeName, roaming);
    }

    [Fact]
    public async Task Location_Bound_User_Cannot_Register_Visit_For_Other_Locations_Employee()
    {
        var fixture = await EnsureFixtureAsync();
        var (deptId, southEmployeeId) = await EnsureScopedEmployeesAsync(fixture);
        Guid purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            purposeId = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == fixture.TenantId)
                .OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync();
        }

        var client = await LoginAsync(NorthUser);
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = $"Cross Location {Guid.NewGuid():N}"[..28],
            gender = 1,
            companyName = "Cross Location Co",
            telephone = "9000000004",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId = deptId,
            hostEmployeeId = southEmployeeId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        Assert.True(body.Contains("another location"), body);
    }

    [Fact]
    public async Task Location_Write_Endpoints_Reject_Reception()
    {
        await EnsureFixtureAsync();
        var client = await LoginAsync(NorthUser);

        var response = await client.PostAsJsonAsync("/api/masters/locations", new
        {
            name = $"Unauthorized Location {Guid.NewGuid():N}",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
