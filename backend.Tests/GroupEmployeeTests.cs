using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Logins are made only from Employee master records and sign in with the face captured at employee
/// registration; employees are recognised by face at any location and get no visitor emails on official visits.
/// </summary>
[Collection("CoreWorkflow")]
public class GroupEmployeeTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly TestApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _createdUserIds = new();

    public GroupEmployeeTests(TestApiFactory factory)
    {
        _baseFactory = factory;
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVisitMailService>();
            services.AddSingleton<IVisitMailService>(_mail);
        }));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => TestUsers.DeactivateAsync(_baseFactory, _createdUserIds);

    [Fact]
    public async Task Admin_Creates_Employee_Login_With_Face_Only_At_Their_Location()
    {
        var adminLocationId = await AdminLocationIdAsync();
        var face = TestPhotos.Base64($"employee-{Guid.NewGuid():N}");
        var employee = await CreateEmployeeAsync(adminLocationId, face);
        var admin = await LoginAsync("admin");

        var username = $"emp{Guid.NewGuid():N}"[..20];
        var response = await admin.PostAsJsonAsync($"/api/users/from-employee/{employee.Id}",
            new { username, password = TestSecrets.SeedPassword });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
        _createdUserIds.Add(created.UserId!);

        var faces = await admin.GetFromJsonAsync<ApiResponse<List<UserFaceLoginDto>>>($"/api/users/{created.UserId}/faces", JsonOptions);
        Assert.Equal(employee.FullName, Assert.Single(faces!.Data!).PersonName);

        await SetUserAsync(created.UserId!, u => u.MustChangePassword = false);
        var signIn = await _factory.CreateClient().PostAsJsonAsync("/api/auth/face-login", new { photoBase64 = face, rememberMe = false });
        Assert.True(signIn.IsSuccessStatusCode, await signIn.Content.ReadAsStringAsync());
        var login = (await signIn.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOptions))!.Data!;
        Assert.Contains(AppRoles.Host, login.User.Roles);

        var self = _factory.CreateClient();
        self.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var details = (await self.GetFromJsonAsync<ApiResponse<MyDetailsDto>>("/api/auth/me/details", JsonOptions))!.Data!;
        Assert.Equal(username, details.Username);
        Assert.Equal(employee.DepartmentName, details.DepartmentName);
        Assert.Equal(employee.Phone, details.Phone);
        Assert.Equal(1, details.FaceLoginCount);
        Assert.Equal(HttpStatusCode.Forbidden, (await self.GetAsync("/api/users")).StatusCode);

        var elsewhere = await CreateEmployeeAsync(null, TestPhotos.Base64($"elsewhere-{Guid.NewGuid():N}"));
        var denied = await admin.PostAsJsonAsync($"/api/users/from-employee/{elsewhere.Id}",
            new { username = $"denied{Guid.NewGuid():N}"[..20], password = TestSecrets.SeedPassword });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Users_Come_Only_From_Employees_In_Any_Role_With_The_Employee_Face()
    {
        var admin = await LoginAsync("admin");
        var adminLocationId = await AdminLocationIdAsync();

        var direct = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Not An Employee",
            username = $"direct{Guid.NewGuid():N}"[..20],
            email = $"direct.{Guid.NewGuid():N}@example.test",
            role = AppRoles.Reception,
            locationId = adminLocationId,
            password = TestSecrets.SeedPassword
        });
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
        Assert.Contains("Employee master", await direct.Content.ReadAsStringAsync());

        var face = TestPhotos.Base64($"desk-{Guid.NewGuid():N}");
        var employee = await CreateEmployeeAsync(adminLocationId, face);
        var username = $"desk{Guid.NewGuid():N}"[..20];
        var response = await admin.PostAsJsonAsync($"/api/users/from-employee/{employee.Id}", new
        {
            username,
            role = AppRoles.Reception,
            allowedMenuKeys = new[] { "dashboard", "visitors" },
            password = TestSecrets.SeedPassword
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var linked = (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
        _createdUserIds.Add(linked.UserId!);

        var users = (await admin.GetFromJsonAsync<ApiResponse<List<UserDto>>>("/api/users", JsonOptions))!.Data!;
        var user = users.Single(u => u.Id == linked.UserId);
        Assert.Equal([AppRoles.Reception], user.Roles);
        Assert.Equal(adminLocationId, user.LocationId);
        Assert.Equal(employee.FullName, user.FullName);
        Assert.Equal(employee.Email, user.Email);
        Assert.Equal(new[] { "dashboard", "visitors" }, user.AllowedMenuKeys);
        Assert.Equal(1, user.FaceLoginCount);
        Assert.Equal(employee.Id, user.EmployeeId);
        Assert.Null(users.Single(u => u.Username == "admin").EmployeeId);

        var newFace = TestPhotos.Base64($"desk-new-{Guid.NewGuid():N}");
        var recapture = await (await LoginAsync("superadmin"))
            .PutAsJsonAsync($"/api/employee-master/{employee.Id}", Profile(employee, adminLocationId, newFace));
        Assert.True(recapture.IsSuccessStatusCode, await recapture.Content.ReadAsStringAsync());

        await SetUserAsync(linked.UserId!, u => u.MustChangePassword = false);
        var anon = _factory.CreateClient();
        var signIn = await anon.PostAsJsonAsync("/api/auth/face-login", new { photoBase64 = newFace, rememberMe = false });
        Assert.True(signIn.IsSuccessStatusCode, await signIn.Content.ReadAsStringAsync());
        Assert.Equal(username, (await signIn.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOptions))!.Data!.User.Username);
        var oldFace = await anon.PostAsJsonAsync("/api/auth/face-login", new { photoBase64 = face, rememberMe = false });
        Assert.False(oldFace.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Employee_Recognised_By_Face_Gets_No_Visitor_Emails()
    {
        var superadmin = await LoginAsync("superadmin");
        var homeLocation = await OtherLocationAsync();
        var face = TestPhotos.Base64($"travelling-{Guid.NewGuid():N}");
        var employee = await CreateEmployeeAsync(homeLocation.Id, face);
        var response = await superadmin.PostAsJsonAsync($"/api/users/from-employee/{employee.Id}",
            new { username = $"emp{Guid.NewGuid():N}"[..20], password = TestSecrets.SeedPassword });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var linked = (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
        _createdUserIds.Add(linked.UserId!);

        var reception = await LoginAsync("reception");
        var search = await reception.PostAsJsonAsync("/api/visitors/employee-face-search", new { photoBase64 = face });
        Assert.True(search.IsSuccessStatusCode, await search.Content.ReadAsStringAsync());
        var match = (await search.Content.ReadFromJsonAsync<ApiResponse<GroupEmployeeMatchDto>>(JsonOptions))!.Data!;
        Assert.Equal(linked.UserId, match.UserId);
        Assert.Equal(employee.FullName, match.FullName);
        Assert.Equal(employee.Email, match.Email);
        Assert.Equal(homeLocation.Name, match.LocationName);

        var stranger = await reception.PostAsJsonAsync("/api/visitors/employee-face-search",
            new { photoBase64 = TestPhotos.Base64($"stranger-{Guid.NewGuid():N}") });
        Assert.True(stranger.IsSuccessStatusCode);
        Assert.Null((await stranger.Content.ReadFromJsonAsync<ApiResponse<GroupEmployeeMatchDto>>(JsonOptions))!.Data);

        var (visitId, visitNumber) = await RegisterAndCheckInAsync(reception, superadmin, match, face);
        var detail = (await superadmin.GetFromJsonAsync<ApiResponse<VisitorDetailDto>>($"/api/visitors/{visitId}", JsonOptions))!.Data!;
        Assert.True(detail.IsGroupEmployee);

        (await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true })).EnsureSuccessStatusCode();
        var checkOut = (await WaitForAsync(() => _mail.CheckOutsFor(visitNumber), "check-out"))[0];
        Assert.Null(checkOut.VisitorEmail);
        Assert.False(string.IsNullOrWhiteSpace(checkOut.VisitingPersonEmail));

        (await superadmin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes",
            new { meetingMinutes = "Internal review.", sendEmail = true })).EnsureSuccessStatusCode();
        Assert.Null((await WaitForAsync(() => _mail.MinutesFor(visitNumber), "minutes"))[0].VisitorEmail);
    }

    private async Task<(Guid VisitId, string VisitNumber)> RegisterAndCheckInAsync(
        HttpClient reception, HttpClient superadmin, GroupEmployeeMatchDto match, string face)
    {
        var settings = (await (await superadmin.GetAsync("/api/settings")).Content
            .ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        settings.ApprovalRequired = false;
        settings.WalkInApprovalRequired = false;
        (await superadmin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();

        Guid departmentId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId && e.Email != null && e.UserId == null)
                .OrderBy(e => e.FullName).FirstAsync();
            var purpose = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId).OrderBy(p => p.SortOrder).FirstAsync();
            (departmentId, hostId, purposeId) = (host.DepartmentId, host.Id, purpose.Id);
        }

        var register = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = match.FullName,
            gender = 1,
            companyName = match.CompanyName,
            telephone = $"96{Random.Shared.Next(10000000, 99999999)}",
            email = match.Email,
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            photoBase64 = face,
            groupEmployeeUserId = match.UserId
        });
        Assert.True(register.IsSuccessStatusCode, await register.Content.ReadAsStringAsync());
        var data = (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var visitId = data.GetProperty("visitId").GetGuid();
        var visitNumber = data.GetProperty("visitNumber").GetString()!;

        if (!string.Equals(data.GetProperty("statusLabel").GetString(), "Inside", StringComparison.OrdinalIgnoreCase))
        {
            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
        }
        return (visitId, visitNumber);
    }

    private async Task<EmployeeDto> CreateEmployeeAsync(Guid? locationId, string face)
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
        var response = await superadmin.PostAsJsonAsync("/api/employee-master", new
        {
            title = EmployeeTitles.Ms,
            // Sorts after the seeded employees, which other tests pick as the first host by name.
            fullName = $"Zz Group Employee {suffix}",
            code = $"GE{suffix}",
            dateOfBirth = "1990-05-17",
            gender = EmployeeGender.Female,
            email = $"group.{suffix}@example.test",
            phone = $"95{Random.Shared.Next(10000000, 99999999)}",
            departmentId,
            locationId,
            isActive = true,
            facePhotoBase64 = face
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>(JsonOptions))!.Data!;
    }

    private static object Profile(EmployeeDto employee, Guid? locationId, string? face) => new
    {
        title = employee.Title,
        fullName = employee.FullName,
        code = employee.Code,
        dateOfBirth = employee.DateOfBirth,
        gender = employee.Gender,
        email = employee.Email,
        phone = employee.Phone,
        departmentId = employee.DepartmentId,
        locationId,
        isActive = true,
        facePhotoBase64 = face
    };

    private async Task<Guid> AdminLocationIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.Users.IgnoreQueryFilters()
            .Where(u => u.UserName == "admin").Select(u => u.LocationId).FirstAsync())!.Value;
    }

    /// <summary>A location other than the reception desk's, so the employee is visiting from elsewhere.</summary>
    private async Task<(Guid Id, string Name)> OtherLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var receptionLocationId = await users.Users.IgnoreQueryFilters()
            .Where(u => u.UserName == "reception").Select(u => u.LocationId).FirstAsync();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var other = await db.Locations.IgnoreQueryFilters()
            .Where(l => l.IsActive && l.TenantId == WellKnownTenants.TiaanoId && l.Id != receptionLocationId)
            .OrderBy(l => l.Name).Select(l => new { l.Id, l.Name }).FirstOrDefaultAsync();
        if (other is not null) return (other.Id, other.Name);

        var created = new Location
        {
            TenantId = WellKnownTenants.TiaanoId,
            Name = $"Branch {Guid.NewGuid():N}"[..20],
            IsActive = true
        };
        db.Locations.Add(created);
        await db.SaveChangesAsync();
        return (created.Id, created.Name);
    }

    private static async Task<List<VisitMailContext>> WaitForAsync(Func<List<VisitMailContext>> read, string what)
    {
        for (var i = 0; i < 50; i++)
        {
            var sent = read();
            if (sent.Count > 0) return sent;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"No {what} email was sent.");
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

    private sealed class RecordingVisitMailService : IVisitMailService
    {
        private readonly ConcurrentQueue<VisitMailContext> _checkOuts = new();
        private readonly ConcurrentQueue<VisitMailContext> _minutes = new();

        public List<VisitMailContext> CheckOutsFor(string visitNumber) =>
            _checkOuts.Where(c => c.VisitNumber == visitNumber).ToList();

        public List<VisitMailContext> MinutesFor(string visitNumber) =>
            _minutes.Where(c => c.VisitNumber == visitNumber).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentCancelledAsync(VisitMailContext ctx, bool hostOnly = false) => Task.CompletedTask;
        public Task<bool> SendVisitorInvitationAsync(VisitMailContext ctx, string idempotencyKey) => Task.FromResult(true);

        public Task SendCheckOutAsync(VisitMailContext ctx)
        {
            _checkOuts.Enqueue(ctx);
            return Task.CompletedTask;
        }

        public Task SendMeetingMinutesAsync(VisitMailContext ctx)
        {
            _minutes.Enqueue(ctx);
            return Task.CompletedTask;
        }
    }
}
