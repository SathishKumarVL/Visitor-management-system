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

/// <summary>
/// The Employee menu: full profiles with a face, Admin limited to their location, recognition before a login
/// exists, and the face carried over to the login.
/// </summary>
[Collection("CoreWorkflow")]
public class EmployeeMasterTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;
    private readonly List<string> _createdUserIds = new();

    public EmployeeMasterTests(TestApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => TestUsers.DeactivateAsync(_factory, _createdUserIds);

    [Fact]
    public async Task Admin_Manages_Full_Employee_Profiles_Only_At_Their_Location()
    {
        var admin = await LoginAsync("admin");
        var adminLocationId = (await LocationOfAsync("admin"))!.Value;
        var otherLocationId = await OtherLocationAsync(adminLocationId);
        var departmentId = await DepartmentIdAsync();
        var face = TestPhotos.Base64($"master-{Guid.NewGuid():N}");

        var elsewhere = await admin.PostAsJsonAsync("/api/employee-master", Profile(departmentId, otherLocationId, face));
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);

        var noDob = Profile(departmentId, null, face);
        noDob.Remove("dateOfBirth");
        await AssertBadRequestAsync(admin.PostAsJsonAsync("/api/employee-master", noDob), "Date of birth is required.");

        var noTitle = Profile(departmentId, null, face);
        noTitle["title"] = "Dr";
        await AssertBadRequestAsync(admin.PostAsJsonAsync("/api/employee-master", noTitle), "Select Mr or Ms.");

        var noFace = Profile(departmentId, null, null);
        await AssertBadRequestAsync(admin.PostAsJsonAsync("/api/employee-master", noFace), "Capture the employee's face.");

        var body = Profile(departmentId, null, face);
        var created = await ReadAsync<EmployeeDto>(await admin.PostAsJsonAsync("/api/employee-master", body));
        Assert.Equal(adminLocationId, created.LocationId);
        Assert.True(created.HasFace);
        Assert.False(string.IsNullOrWhiteSpace(created.PhotoUrl));
        Assert.Equal(new DateOnly(1990, 4, 12), created.DateOfBirth);
        Assert.Equal(EmployeeGender.Female, created.Gender);
        Assert.Equal(EmployeeTitles.Ms, created.Title);

        var photo = await admin.GetAsync(created.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);

        var sameCode = Profile(departmentId, null, TestPhotos.Base64($"other-{Guid.NewGuid():N}"));
        sameCode["code"] = created.Code;
        await AssertBadRequestAsync(admin.PostAsJsonAsync("/api/employee-master", sameCode), $"Employee ID '{created.Code}' is already used");

        var sameFace = Profile(departmentId, null, face);
        await AssertBadRequestAsync(admin.PostAsJsonAsync("/api/employee-master", sameFace), "already enrolled for the employee");

        var edit = Profile(departmentId, adminLocationId, null);
        edit["code"] = created.Code;
        edit["fullName"] = created.FullName + " Edited";
        edit["title"] = "mr.";
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/employee-master/{created.Id}", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/employee-master/{created.Id}/deactivate", null)).StatusCode);

        var superadmin = await LoginAsync("superadmin");
        var updated = await ReadAsync<EmployeeDto>(await superadmin.PutAsJsonAsync($"/api/employee-master/{created.Id}", edit));
        Assert.EndsWith("Edited", updated.FullName);
        Assert.Equal(EmployeeTitles.Mr, updated.Title);
        Assert.Equal(adminLocationId, updated.LocationId);
        Assert.True(updated.HasFace);
        Assert.True(updated.IsActive);

        edit["isActive"] = false;
        Assert.False((await ReadAsync<EmployeeDto>(await superadmin.PutAsJsonAsync($"/api/employee-master/{created.Id}", edit))).IsActive);
        edit["isActive"] = true;
        Assert.True((await ReadAsync<EmployeeDto>(await superadmin.PutAsJsonAsync($"/api/employee-master/{created.Id}", edit))).IsActive);

        var startsInactive = Profile(departmentId, null, TestPhotos.Base64($"new-{Guid.NewGuid():N}"));
        startsInactive["isActive"] = false;
        Assert.True((await ReadAsync<EmployeeDto>(await admin.PostAsJsonAsync("/api/employee-master", startsInactive))).IsActive);

        var list = await ReadAsync<List<EmployeeDto>>(await admin.GetAsync("/api/employee-master"));
        Assert.Contains(list, e => e.Id == created.Id);
        Assert.All(list, e => Assert.Equal(adminLocationId, e.LocationId));

        var away = await ReadAsync<EmployeeDto>(await superadmin.PostAsJsonAsync("/api/employee-master",
            Profile(departmentId, otherLocationId, TestPhotos.Base64($"away-{Guid.NewGuid():N}"))));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsync($"/api/employee-master/{away.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync($"/api/users/from-employee/{away.Id}",
                new { username = $"no{Guid.NewGuid():N}"[..20], password = TestSecrets.SeedPassword })).StatusCode);
    }

    [Fact]
    public async Task Employee_Without_Login_Is_Recognised_And_Their_Login_Gets_The_Face()
    {
        var superadmin = await LoginAsync("superadmin");
        var receptionLocationId = await LocationOfAsync("reception");
        var homeLocationId = await OtherLocationAsync(receptionLocationId);
        var face = TestPhotos.Base64($"visiting-{Guid.NewGuid():N}");
        var employee = await ReadAsync<EmployeeDto>(await superadmin.PostAsJsonAsync("/api/employee-master",
            Profile(await DepartmentIdAsync(), homeLocationId, face)));

        var reception = await LoginAsync("reception");
        var match = await SearchAsync(reception, face);
        Assert.NotNull(match);
        Assert.Equal(employee.Id, match!.EmployeeId);
        Assert.Null(match.UserId);
        Assert.Equal(employee.FullName, match.FullName);
        Assert.Equal(employee.Code, match.EmployeeCode);
        Assert.Equal(employee.LocationName, match.LocationName);

        var visitId = await RegisterAsync(reception, superadmin, match, face);
        var detail = (await superadmin.GetFromJsonAsync<ApiResponse<VisitorDetailDto>>($"/api/visitors/{visitId}", JsonOptions))!.Data!;
        Assert.True(detail.IsGroupEmployee);

        var username = $"emp{Guid.NewGuid():N}"[..20];
        var response = await superadmin.PostAsJsonAsync($"/api/users/from-employee/{employee.Id}",
            new { username, password = TestSecrets.SeedPassword });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!;
        _createdUserIds.Add(result.Data!.UserId!);
        Assert.Contains("face sign-in", result.Message);

        var faces = await superadmin.GetFromJsonAsync<ApiResponse<List<UserFaceLoginDto>>>(
            $"/api/users/{result.Data.UserId}/faces", JsonOptions);
        Assert.Equal(employee.FullName, Assert.Single(faces!.Data!).PersonName);

        await SetUserAsync(result.Data.UserId!, u => u.MustChangePassword = false);
        var signIn = await _factory.CreateClient().PostAsJsonAsync("/api/auth/face-login", new { photoBase64 = face, rememberMe = false });
        Assert.True(signIn.IsSuccessStatusCode, await signIn.Content.ReadAsStringAsync());
        Assert.Equal(username, (await signIn.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOptions))!.Data!.User.Username);

        var again = await SearchAsync(reception, face);
        Assert.NotNull(again);
        Assert.Equal(result.Data.UserId, again!.UserId);
        Assert.Equal(employee.Id, again.EmployeeId);
    }

    private static Dictionary<string, object?> Profile(Guid departmentId, Guid? locationId, string? face)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new Dictionary<string, object?>
        {
            ["title"] = EmployeeTitles.Ms,
            // Sorts after the seeded employees, which other tests pick as the first host by name.
            ["fullName"] = $"Zz Employee Master {suffix}",
            ["code"] = $"EM{suffix}",
            ["dateOfBirth"] = "1990-04-12",
            ["gender"] = (int)EmployeeGender.Female,
            ["phone"] = $"94{Random.Shared.Next(10000000, 99999999)}",
            ["email"] = $"master.{suffix}@example.test",
            ["designation"] = "Engineer",
            ["departmentId"] = departmentId,
            ["locationId"] = locationId,
            ["isActive"] = true,
            ["facePhotoBase64"] = face
        };
    }

    private static async Task<GroupEmployeeMatchDto?> SearchAsync(HttpClient client, string face)
    {
        var search = await client.PostAsJsonAsync("/api/visitors/employee-face-search", new { photoBase64 = face });
        Assert.True(search.IsSuccessStatusCode, await search.Content.ReadAsStringAsync());
        return (await search.Content.ReadFromJsonAsync<ApiResponse<GroupEmployeeMatchDto>>(JsonOptions))!.Data;
    }

    private async Task<Guid> RegisterAsync(HttpClient reception, HttpClient superadmin, GroupEmployeeMatchDto match, string face)
    {
        var settings = (await (await superadmin.GetAsync("/api/settings")).Content
            .ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        settings.ApprovalRequired = false;
        settings.WalkInApprovalRequired = false;
        (await superadmin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();

        var receptionLocationId = await LocationOfAsync("reception");
        Guid departmentId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId && e.Email != null && e.UserId == null
                    && (e.LocationId == null || e.LocationId == receptionLocationId))
                .OrderBy(e => e.FullName).FirstAsync();
            var purpose = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId).OrderBy(p => p.SortOrder).FirstAsync();
            (departmentId, hostId, purposeId) = (host.DepartmentId, host.Id, purpose.Id);
        }

        var register = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = match.FullName,
            gender = 2,
            companyName = match.CompanyName,
            telephone = match.Phone,
            email = match.Email,
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            photoBase64 = face,
            groupEmployeeId = match.EmployeeId
        });
        Assert.True(register.IsSuccessStatusCode, await register.Content.ReadAsStringAsync());
        return (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("visitId").GetGuid();
    }

    private static async Task AssertBadRequestAsync(Task<HttpResponseMessage> call, string expected)
    {
        var response = await call;
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {text}");
        Assert.Contains(expected, JsonSerializer.Deserialize<ApiResponse<object>>(text, JsonOptions)!.Message);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;
    }

    private async Task<Guid> DepartmentIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Departments.IgnoreQueryFilters()
            .Where(d => d.IsActive && d.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(d => d.Name).Select(d => d.Id).FirstAsync();
    }

    private async Task<Guid?> LocationOfAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.Users.IgnoreQueryFilters()
            .Where(u => u.UserName == username).Select(u => u.LocationId).FirstAsync();
    }

    private async Task<Guid> OtherLocationAsync(Guid? notThis)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var other = await db.Locations.IgnoreQueryFilters()
            .Where(l => l.IsActive && l.TenantId == WellKnownTenants.TiaanoId && l.Id != notThis)
            .OrderBy(l => l.Name).Select(l => (Guid?)l.Id).FirstOrDefaultAsync();
        if (other is Guid id) return id;

        var created = new Location { TenantId = WellKnownTenants.TiaanoId, Name = $"Branch {Guid.NewGuid():N}"[..20], IsActive = true };
        db.Locations.Add(created);
        await db.SaveChangesAsync();
        return created.Id;
    }

    private async Task SetUserAsync(string userId, Action<ApplicationUser> change)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        change(user);
        await users.UpdateAsync(user);
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
