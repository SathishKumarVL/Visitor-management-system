using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services.Face;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Recognising a returning visitor, exercised over HTTP. Face templates are derived on the server
/// from the uploaded photo, so these tests post images and never a vector — which is also the point:
/// there is no longer an endpoint that accepts a client-supplied template.
/// </summary>
public class FaceRevisitTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FaceRevisitTests(TestApiFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
            Assert.NotNull(user);
            var token = await users.GeneratePasswordResetTokenAsync(user!);
            await users.ResetPasswordAsync(user!, token, TestSecrets.SeedPassword);
            user!.MustChangePassword = false;
            user.IsActive = true;
            await users.UpdateAsync(user);
        }

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = TestSecrets.SeedPassword,
            rememberMe = false
        });
        login.EnsureSuccessStatusCode();
        var json = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var jwt = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private async Task<(Guid DepartmentId, Guid HostId, Guid PurposeId)> RefsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var host = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(e => e.FullName)
            .FirstAsync();
        var purpose = await db.VisitPurposes.IgnoreQueryFilters()
            .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(p => p.SortOrder)
            .FirstAsync();
        return (host.DepartmentId, host.Id, purpose.Id);
    }

    private async Task<JsonElement> RegisterAsync(
        HttpClient client, string name, string? photoBase64, Guid? recognizedVisitorId = null)
    {
        var (departmentId, hostId, purposeId) = await RefsAsync();
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            companyName = "Face Revisit Co",
            telephone = "9000000002",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            recognizedVisitorId,
            photoBase64
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    private async Task<JsonElement> FaceSearchAsync(HttpClient client, string photoBase64)
    {
        var response = await client.PostAsJsonAsync("/api/visitors/face-search", new { photoBase64 });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    [Fact]
    public async Task Registering_With_A_Photo_Enrols_A_Face_Template()
    {
        var reception = await LoginAsync("reception");
        var photo = TestPhotos.Base64($"enrol-{Guid.NewGuid():N}");

        var visit = await RegisterAsync(reception, "Face Enrol Visitor", photo);
        var visitorId = visit.GetProperty("visitorId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var embedder = _factory.Services.GetRequiredService<IFaceEmbeddingService>();

        var template = await db.VisitorFaceDescriptors.IgnoreQueryFilters()
            .SingleAsync(f => f.VisitorId == visitorId);

        Assert.Equal(embedder.ModelId, template.Model);
        Assert.Equal(embedder.Dimensions, template.Dimensions);
    }

    [Fact]
    public async Task Enrolled_Face_Is_Recognised_On_The_Next_Visit()
    {
        var reception = await LoginAsync("reception");
        var photo = TestPhotos.Base64($"return-{Guid.NewGuid():N}");

        await RegisterAsync(reception, "Face Return Visitor", photo);

        var match = await FaceSearchAsync(reception, photo);

        Assert.Equal(JsonValueKind.Object, match.ValueKind);
        Assert.Equal("Face Return Visitor", match.GetProperty("visitorName").GetString());
        Assert.True(match.GetProperty("similarity").GetDouble() > 0.6);
    }

    [Fact]
    public async Task A_Different_Face_Is_Not_Matched()
    {
        var reception = await LoginAsync("reception");
        await RegisterAsync(reception, "Face Baseline Visitor", TestPhotos.Base64($"baseline-{Guid.NewGuid():N}"));

        var stranger = await FaceSearchAsync(reception, TestPhotos.Base64($"stranger-{Guid.NewGuid():N}"));

        Assert.Equal(JsonValueKind.Null, stranger.ValueKind);
    }

    /// <summary>A confirmed match must join the existing visitor's history rather than fork a record.</summary>
    [Fact]
    public async Task Confirmed_Match_Reuses_The_Visitor_Instead_Of_Duplicating()
    {
        var reception = await LoginAsync("reception");
        var photo = TestPhotos.Base64($"reuse-{Guid.NewGuid():N}");

        await RegisterAsync(reception, "Face Reuse Visitor", photo);
        var match = await FaceSearchAsync(reception, photo);
        var visitorId = match.GetProperty("visitorId").GetGuid();

        await RegisterAsync(reception, "Face Reuse Visitor", photo, recognizedVisitorId: visitorId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(2, await db.VisitorVisits.IgnoreQueryFilters().CountAsync(v => v.VisitorId == visitorId));
        Assert.Equal(1, await db.Visitors.IgnoreQueryFilters().CountAsync(v => v.Id == visitorId));
    }

    [Fact]
    public async Task Repeat_Visits_Accumulate_Templates_Up_To_The_Cap()
    {
        var reception = await LoginAsync("reception");
        var identity = $"cap-{Guid.NewGuid():N}";

        var first = await RegisterAsync(reception, "Face Template Visitor", TestPhotos.Base64(identity));
        var visitorId = first.GetProperty("visitorId").GetGuid();

        // Each visit contributes a distinct capture, so the rolling window is what bounds the total.
        for (var i = 0; i < FaceRecognition.MaxTemplatesPerVisitor + 2; i++)
            await RegisterAsync(reception, "Face Template Visitor", TestPhotos.Base64($"{identity}-{i}"), visitorId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var templates = await db.VisitorFaceDescriptors.IgnoreQueryFilters()
            .CountAsync(f => f.VisitorId == visitorId);

        Assert.Equal(FaceRecognition.MaxTemplatesPerVisitor, templates);
    }

    /// <summary>Registration must still succeed when the photo yields no usable face.</summary>
    [Fact]
    public async Task Unusable_Photo_Does_Not_Block_Registration()
    {
        var reception = await LoginAsync("reception");

        var visit = await RegisterAsync(reception, "Face Optional Visitor", photoBase64: null);

        Assert.NotEqual(Guid.Empty, visit.GetProperty("visitId").GetGuid());
    }
}
