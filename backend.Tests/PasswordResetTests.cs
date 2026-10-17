using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Sign-in by email or any letter case, self-service password reset and admin reset.</summary>
[Collection("CoreWorkflow")]
public class PasswordResetTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private const string NewPassword = "Fresh!Passw0rd-42";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;
    private readonly List<string> _createdUserIds = new();

    public PasswordResetTests(TestApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Created users would otherwise count against the license's active-user limit on every run.</summary>
    public Task DisposeAsync() => TestUsers.DeactivateAsync(_factory, _createdUserIds);

    [Fact]
    public async Task Login_Accepts_Email_And_Any_Case_Username()
    {
        var (username, email, _) = await CreateUserAsync();
        var client = _factory.CreateClient();

        var upper = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "  " + username.ToUpperInvariant() + " ", password = TestSecrets.SeedPassword, rememberMe = false });
        Assert.True(upper.IsSuccessStatusCode, await upper.Content.ReadAsStringAsync());

        var byEmail = await client.PostAsJsonAsync("/api/auth/login",
            new { username = email.ToUpperInvariant(), password = TestSecrets.SeedPassword, rememberMe = false });
        Assert.True(byEmail.IsSuccessStatusCode, await byEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Change_Password_Rejects_Reusing_The_Current_Password()
    {
        var (username, _, _) = await CreateUserAsync();
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var same = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = TestSecrets.SeedPassword, newPassword = TestSecrets.SeedPassword });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains("New password cannot be the same as the current password.", await same.Content.ReadAsStringAsync());

        var wrong = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Not-The-Password-1!", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Current password is incorrect.", await wrong.Content.ReadAsStringAsync());

        var changed = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = TestSecrets.SeedPassword, newPassword = NewPassword });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forgot_Password_Gives_Same_Answer_For_Known_And_Unknown_Accounts()
    {
        var (username, _, _) = await CreateUserAsync();
        var client = _factory.CreateClient();

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { usernameOrEmail = username });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { usernameOrEmail = $"nobody{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        var empty = await client.PostAsJsonAsync("/api/auth/forgot-password", new { usernameOrEmail = "" });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("username or email", await empty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_Link_Changes_Password_Once()
    {
        var (username, _, userId) = await CreateUserAsync();
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
            token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        }

        var client = _factory.CreateClient();
        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, newPassword = NewPassword });
        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = NewPassword, rememberMe = false });
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());

        var reuse = await client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, newPassword = NewPassword + "x" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Contains("invalid or has expired", await reuse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_Resets_Password_But_Not_For_SuperAdmin()
    {
        var (username, _, userId) = await CreateUserAsync();
        var admin = await LoginAsync("admin");

        var reset = await admin.PostAsJsonAsync($"/api/users/{userId}/reset-password",
            new { newPassword = NewPassword, mustChangePassword = true });
        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = NewPassword, rememberMe = false });
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        var json = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.GetProperty("data").GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        string superAdminId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            superAdminId = (await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "superadmin")).Id;
        }
        var denied = await admin.PostAsJsonAsync($"/api/users/{superAdminId}/reset-password",
            new { newPassword = NewPassword, mustChangePassword = true });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<(string Username, string Email, string Id)> CreateUserAsync()
    {
        var superadmin = await LoginAsync("superadmin");
        var username = $"pwd{Guid.NewGuid():N}"[..18];
        var email = $"{username}@example.test";
        Guid? adminLocationId;
        using (var lookup = _factory.Services.CreateScope())
        {
            adminLocationId = await lookup.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users
                .IgnoreQueryFilters().Where(u => u.UserName == "admin").Select(u => u.LocationId).FirstAsync();
        }
        var employeeId = await TestUsers.CreateEmployeeAsync(_factory.Services, WellKnownTenants.TiaanoId, adminLocationId);
        var response = await superadmin.PostAsJsonAsync($"/api/users/from-employee/{employeeId}", new
        {
            username,
            email,
            role = "Reception",
            password = TestSecrets.SeedPassword
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == username);
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        _createdUserIds.Add(user.Id);
        return (username, email, user.Id);
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
