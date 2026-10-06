using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Optional face sign-in: several people can share one login, each with their own registered face.</summary>
[Collection("CoreWorkflow")]
public class FaceLoginTests : IClassFixture<TestApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TestApiFactory _factory;
    private readonly List<string> _createdUserIds = new();

    public FaceLoginTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Two_Guards_Sign_In_To_The_Same_Login_With_Their_Own_Faces()
    {
        var admin = await LoginAsync("admin");
        var login = await CreateUserAsync(admin, AppRoles.Security);
        var guardA = $"guard-a-{Guid.NewGuid():N}";
        var guardB = $"guard-b-{Guid.NewGuid():N}";

        await AddFaceAsync(admin, login.Id, "Ravi", TestPhotos.Base64(guardA));
        await AddFaceAsync(admin, login.Id, "Kumar", TestPhotos.Base64(guardB));

        var faces = await admin.GetFromJsonAsync<ApiResponse<List<UserFaceLoginDto>>>($"/api/users/{login.Id}/faces", JsonOptions);
        Assert.Equal(new[] { "Kumar", "Ravi" }, faces!.Data!.Select(f => f.PersonName).ToArray());

        var users = await admin.GetFromJsonAsync<ApiResponse<List<UserDto>>>("/api/users", JsonOptions);
        Assert.Equal(2, users!.Data!.Single(u => u.Id == login.Id).FaceLoginCount);

        var status = await _factory.CreateClient().GetFromJsonAsync<ApiResponse<FaceLoginStatusDto>>("/api/auth/face-login/status", JsonOptions);
        Assert.True(status!.Data!.Available);

        foreach (var (identity, person) in new[] { (guardA, "Ravi"), (guardB, "Kumar") })
        {
            var response = await FaceLoginAsync(TestPhotos.Base64(identity));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var body = (await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOptions))!.Data!;
            Assert.Equal(login.Id, body.User.Id);
            Assert.Equal(person, body.FacePersonName);
            Assert.False(string.IsNullOrWhiteSpace(body.Token));

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
            var me = await client.GetFromJsonAsync<ApiResponse<UserDto>>("/api/auth/me", JsonOptions);
            Assert.Equal(login.Username, me!.Data!.Username);
        }

        var after = await admin.GetFromJsonAsync<ApiResponse<List<UserFaceLoginDto>>>($"/api/users/{login.Id}/faces", JsonOptions);
        Assert.All(after!.Data!, f => Assert.NotNull(f.LastUsedAt));
    }

    [Fact]
    public async Task Unknown_Face_Or_Bad_Photo_Does_Not_Sign_In()
    {
        var admin = await LoginAsync("admin");
        var login = await CreateUserAsync(admin, AppRoles.Reception);
        await AddFaceAsync(admin, login.Id, "Enrolled", TestPhotos.Base64($"enrolled-{Guid.NewGuid():N}"));

        var stranger = await FaceLoginAsync(TestPhotos.Base64($"stranger-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Contains("Face not recognised", await stranger.Content.ReadAsStringAsync());

        var noFace = await FaceLoginAsync(TestPhotos.NoFaceBase64());
        Assert.Equal(HttpStatusCode.BadRequest, noFace.StatusCode);

        var twoFaces = await FaceLoginAsync(TestPhotos.TwoFacesBase64());
        Assert.Equal(HttpStatusCode.BadRequest, twoFaces.StatusCode);
    }

    [Fact]
    public async Task Removed_Face_And_Disabled_Login_Can_No_Longer_Sign_In()
    {
        var admin = await LoginAsync("admin");
        var login = await CreateUserAsync(admin, AppRoles.Security);
        var keep = $"keep-{Guid.NewGuid():N}";
        var removed = $"removed-{Guid.NewGuid():N}";
        await AddFaceAsync(admin, login.Id, "Keeps access", TestPhotos.Base64(keep));
        var toRemove = await AddFaceAsync(admin, login.Id, "Left the company", TestPhotos.Base64(removed));

        (await admin.DeleteAsync($"/api/users/{login.Id}/faces/{toRemove.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await FaceLoginAsync(TestPhotos.Base64(removed))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FaceLoginAsync(TestPhotos.Base64(keep))).StatusCode);

        await SetUserAsync(login.Id, u => u.IsActive = false);
        var disabled = await FaceLoginAsync(TestPhotos.Base64(keep));
        Assert.Equal(HttpStatusCode.BadRequest, disabled.StatusCode);
        Assert.Contains("disabled", await disabled.Content.ReadAsStringAsync());

        await SetUserAsync(login.Id, u => { u.IsActive = true; u.MustChangePassword = true; });
        var mustChange = await FaceLoginAsync(TestPhotos.Base64(keep));
        Assert.Equal(HttpStatusCode.BadRequest, mustChange.StatusCode);
        Assert.Contains("new password first", await mustChange.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task One_Face_Cannot_Be_Registered_For_Two_Logins()
    {
        var admin = await LoginAsync("admin");
        var first = await CreateUserAsync(admin, AppRoles.Security);
        var second = await CreateUserAsync(admin, AppRoles.Reception);
        var photo = TestPhotos.Base64($"shared-{Guid.NewGuid():N}");

        await AddFaceAsync(admin, first.Id, "Same person", photo);
        var clash = await admin.PostAsJsonAsync($"/api/users/{second.Id}/faces", new { personName = "Same person", photoBase64 = photo });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Contains(first.Username, await clash.Content.ReadAsStringAsync());

        var missingName = await admin.PostAsJsonAsync($"/api/users/{second.Id}/faces",
            new { personName = "  ", photoBase64 = TestPhotos.Base64($"x-{Guid.NewGuid():N}") });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
    }

    [Fact]
    public async Task Only_Admins_Manage_Faces_And_Only_SuperAdmin_For_A_SuperAdmin()
    {
        var admin = await LoginAsync("admin");
        var reception = await LoginAsync("reception");
        var target = await CreateUserAsync(admin, AppRoles.Security);

        var denied = await reception.PostAsJsonAsync($"/api/users/{target.Id}/faces",
            new { personName = "Nope", photoBase64 = TestPhotos.Base64($"nope-{Guid.NewGuid():N}") });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        string superAdminId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            superAdminId = (await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "superadmin")).Id;
        }

        var adminOnSuper = await admin.PostAsJsonAsync($"/api/users/{superAdminId}/faces",
            new { personName = "Owner", photoBase64 = TestPhotos.Base64($"owner-{Guid.NewGuid():N}") });
        Assert.Equal(HttpStatusCode.Forbidden, adminOnSuper.StatusCode);

        var superadmin = await LoginAsync("superadmin");
        var ownerFace = TestPhotos.Base64($"owner-{Guid.NewGuid():N}");
        var added = await AddFaceAsync(superadmin, superAdminId, "Owner", ownerFace);
        var signIn = await FaceLoginAsync(ownerFace);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        (await superadmin.DeleteAsync($"/api/users/{superAdminId}/faces/{added.Id}")).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> FaceLoginAsync(string photoBase64) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/face-login", new { photoBase64, rememberMe = false });

    private async Task<UserFaceLoginDto> AddFaceAsync(HttpClient client, string userId, string personName, string photoBase64)
    {
        var response = await client.PostAsJsonAsync($"/api/users/{userId}/faces", new { personName, photoBase64 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UserFaceLoginDto>>(JsonOptions))!.Data!;
    }

    /// <summary>Created directly so the tests do not consume the license's active-user allowance.</summary>
    private async Task<UserDto> CreateUserAsync(HttpClient admin, string role)
    {
        _ = admin;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"face{suffix}",
            Email = $"face.{suffix}@example.test",
            FullName = $"Face Login {suffix}",
            TenantId = WellKnownTenants.TiaanoId,
            IsActive = true,
            MustChangePassword = false,
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(user, TestSecrets.SeedPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        _createdUserIds.Add(user.Id);
        return new UserDto { Id = user.Id, Username = user.UserName, FullName = user.FullName };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => TestUsers.DeactivateAsync(_factory, _createdUserIds);

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
