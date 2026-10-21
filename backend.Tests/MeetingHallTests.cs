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
using Tiaano.Vms.Api.Security;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Meeting halls are allocated at check-in: the smallest free hall at the visit's location that seats the party,
/// rotating between equal halls, freed at check-out. Halls of 6+ seats are kept for groups of 6+ unless no smaller
/// hall could seat the party. Each test works in its own new location so halls and
/// visits left behind by other runs cannot interfere.
/// </summary>
[Collection("CoreWorkflow")]
public class MeetingHallTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private readonly TestApiFactory _factory;
    private readonly List<string> _createdUserIds = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MeetingHallTests(TestApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => TestUsers.DeactivateAsync(_factory, _createdUserIds);

    [Fact]
    public async Task Smallest_Free_Hall_That_Seats_The_Party_Is_Allocated_And_Freed_At_CheckOut()
    {
        var admin = await AdminAsync();
        var location = await CreateLocationAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        await CreateHallAsync(admin, $"BIG-{tag}", "Board Room", 10, location);
        var small = await CreateHallAsync(admin, $"SML-{tag}", "Huddle Room", 4, location);
        await CreateHallAsync(admin, $"TNY-{tag}", "Phone Booth", 2, location);

        var first = await RegisterAsync(admin, location, persons: 3);
        var firstPass = await CheckInAsync(admin, first);
        Assert.Equal("Huddle Room", firstPass.MeetingHallName);
        Assert.Null(firstPass.MeetingHallWarning);

        var halls = await HallsAsync(admin, location);
        Assert.NotNull(halls.Single(h => h.Id == small.Id).OccupiedByVisitNumber);

        var second = await RegisterAsync(admin, location, persons: 3);
        var secondPass = await CheckInAsync(admin, second);
        Assert.Null(secondPass.MeetingHallName);
        Assert.Contains("are in use", secondPass.MeetingHallWarning);
        Assert.Contains("kept for larger groups", secondPass.MeetingHallWarning);

        var group = await RegisterAsync(admin, location, persons: 8);
        Assert.Equal("Board Room", (await CheckInAsync(admin, group)).MeetingHallName);
        await CheckOutAsync(admin, group);

        var five = await RegisterAsync(admin, location, persons: 5);
        Assert.Equal("Board Room", (await CheckInAsync(admin, five)).MeetingHallName);
        await CheckOutAsync(admin, five);

        await CheckOutAsync(admin, first);
        var fourth = await RegisterAsync(admin, location, persons: 3);
        Assert.Equal("Huddle Room", (await CheckInAsync(admin, fourth)).MeetingHallName);

        var crowd = await RegisterAsync(admin, location, persons: 20);
        var crowdPass = await CheckInAsync(admin, crowd);
        Assert.Null(crowdPass.MeetingHallName);
        Assert.Contains("No meeting hall seats 20", crowdPass.MeetingHallWarning);

        var detail = await admin.GetFromJsonAsync<ApiResponse<VisitorDetailDto>>($"/api/visitors/{first}", JsonOptions);
        Assert.Equal("Huddle Room", detail!.Data!.MeetingHallName);

        foreach (var visit in new[] { second, fourth, crowd })
            await CheckOutAsync(admin, visit);
    }

    [Fact]
    public async Task Halls_Of_Equal_Size_Take_Turns()
    {
        var admin = await AdminAsync();
        var location = await CreateLocationAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        await CreateHallAsync(admin, $"A-{tag}", "Hall A", 4, location);
        await CreateHallAsync(admin, $"B-{tag}", "Hall B", 4, location);

        var picks = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            var visit = await RegisterAsync(admin, location, persons: 2);
            picks.Add((await CheckInAsync(admin, visit)).MeetingHallName);
            await CheckOutAsync(admin, visit);
        }

        Assert.Equal(new[] { "Hall A", "Hall B", "Hall A" }, picks);
    }

    [Fact]
    public async Task Location_With_No_Halls_Checks_In_Without_A_Warning()
    {
        var admin = await AdminAsync();
        var location = await CreateLocationAsync();

        var visit = await RegisterAsync(admin, location, persons: 1);
        var pass = await CheckInAsync(admin, visit);

        Assert.Null(pass.MeetingHallName);
        Assert.Null(pass.MeetingHallWarning);
        await CheckOutAsync(admin, visit);
    }

    [Fact]
    public async Task Location_Bound_Admin_Manages_Only_Their_Own_Halls()
    {
        var admin = await AdminAsync();
        var home = await CreateLocationAsync();
        var other = await CreateLocationAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var otherHall = await CreateHallAsync(admin, $"OTH-{tag}", "Other Hall", 8, other);

        var bound = await CreateBoundAdminAsync(home);

        var own = await bound.PostAsJsonAsync("/api/masters/meeting-halls",
            new { code = $"OWN-{tag}", name = "Own Hall", seatingCapacity = 5 });
        Assert.True(own.IsSuccessStatusCode, await own.Content.ReadAsStringAsync());
        var created = (await own.Content.ReadFromJsonAsync<ApiResponse<MeetingHallDto>>(JsonOptions))!.Data!;
        Assert.Equal(home, created.LocationId);

        var list = await HallsAsync(bound, null);
        Assert.Contains(list, h => h.Id == created.Id);
        Assert.DoesNotContain(list, h => h.LocationId != home);

        var elsewhere = await bound.PostAsJsonAsync("/api/masters/meeting-halls",
            new { code = $"BAD-{tag}", name = "Not Mine", seatingCapacity = 5, locationId = other });
        Assert.False(elsewhere.IsSuccessStatusCode);
        Assert.Contains("another location", await elsewhere.Content.ReadAsStringAsync());

        var edit = await bound.PutAsJsonAsync($"/api/masters/meeting-halls/{otherHall.Id}",
            new { code = otherHall.Code, name = "Taken over", seatingCapacity = 8, locationId = home });
        Assert.False(edit.IsSuccessStatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/masters/meeting-halls",
            new { code = $"OWN-{tag}", name = "Copy", seatingCapacity = 3, locationId = home });
        Assert.False(duplicate.IsSuccessStatusCode);
        Assert.Contains("already in use", await duplicate.Content.ReadAsStringAsync());

        var noSeats = await admin.PostAsJsonAsync("/api/masters/meeting-halls",
            new { code = $"ZERO-{tag}", name = "No seats", seatingCapacity = 0, locationId = home });
        Assert.False(noSeats.IsSuccessStatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<HttpClient> AdminAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "superadmin");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, token, TestSecrets.SeedPassword);
            user.MustChangePassword = false;
            user.IsActive = true;
            await users.UpdateAsync(user);
        }
        var client = await LoginAsync("superadmin", TestSecrets.SeedPassword);

        var settings = (await client.GetFromJsonAsync<ApiResponse<SettingsDto>>("/api/settings", JsonOptions))!.Data!;
        if (settings.ApprovalRequired || settings.WalkInApprovalRequired)
        {
            settings.ApprovalRequired = false;
            settings.WalkInApprovalRequired = false;
            (await client.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();
        }
        return client;
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password, rememberMe = false });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }

    private async Task<HttpClient> CreateBoundAdminAsync(Guid locationId)
    {
        var username = $"hall{Guid.NewGuid():N}"[..20];
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@example.test",
                EmailConfirmed = true,
                FullName = "Hall Admin",
                TenantId = WellKnownTenants.TiaanoId,
                LocationId = locationId,
                IsActive = true,
                MustChangePassword = false
            };
            var created = await users.CreateAsync(user, TestSecrets.SeedPassword);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, AppRoles.Admin);
            _createdUserIds.Add(user.Id);
        }
        return await LoginAsync(username, TestSecrets.SeedPassword);
    }

    private async Task<Guid> CreateLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var location = new Location
        {
            TenantId = WellKnownTenants.TiaanoId,
            Name = $"Hall Test {Guid.NewGuid():N}"[..30],
            IsActive = true,
            SortOrder = 999
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private static async Task<MeetingHallDto> CreateHallAsync(HttpClient client, string code, string name, int seats, Guid locationId)
    {
        var response = await client.PostAsJsonAsync("/api/masters/meeting-halls",
            new { code, name, seatingCapacity = seats, intercomNo = "2101", locationId, isActive = true });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<MeetingHallDto>>(JsonOptions))!.Data!;
    }

    private static async Task<List<MeetingHallDto>> HallsAsync(HttpClient client, Guid? locationId)
    {
        var url = "/api/masters/meeting-halls?activeOnly=false" + (locationId is Guid id ? $"&locationId={id}" : "");
        return (await client.GetFromJsonAsync<ApiResponse<List<MeetingHallDto>>>(url, JsonOptions))!.Data!;
    }

    private async Task<Guid> RegisterAsync(HttpClient client, Guid locationId, int persons)
    {
        Guid departmentId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(e => e.FullName).FirstAsync();
            var purpose = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(p => p.SortOrder).FirstAsync();
            (departmentId, hostId, purposeId) = (host.DepartmentId, host.Id, purpose.Id);
        }

        var name = $"Hall Visitor {Guid.NewGuid():N}"[..24];
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            gender = 1,
            companyName = "Hall Test Co",
            telephone = "9000000002",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = new[] { locationId },
            numberOfPersons = persons,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            photoBase64 = TestPhotos.Base64($"{name}-{Guid.NewGuid():N}")
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data").GetProperty("visitId").GetGuid();
    }

    private static async Task<PassDto> CheckInAsync(HttpClient client, Guid visitId)
    {
        var response = await client.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PassDto>>(JsonOptions))!.Data!;
    }

    private static async Task CheckOutAsync(HttpClient client, Guid visitId)
    {
        var questions = await client.GetFromJsonAsync<JsonElement>("/api/masters/feedback-questions?activeOnly=true", JsonOptions);
        var answers = questions.GetProperty("data").EnumerateArray()
            .Where(q => !q.TryGetProperty("locationId", out var loc) || loc.ValueKind == JsonValueKind.Null)
            .Select(q => new { questionId = q.GetProperty("id").GetGuid(), rating = 5 })
            .ToArray();
        if (answers.Length > 0)
            (await client.PostAsJsonAsync($"/api/visitors/{visitId}/feedback", new { answers })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
