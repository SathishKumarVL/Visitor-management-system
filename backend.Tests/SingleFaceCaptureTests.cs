using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Tiaano.Vms.Api.Services.Face;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Exactly-one-face visitor photo gate: 0 / 1 / 2+ faces, no permanent storage on reject,
/// and backend enforcement that cannot be bypassed by skipping the camera UI.
/// </summary>
[Collection("CoreWorkflow")]
public class SingleFaceCaptureTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SingleFaceCaptureTests(TestApiFactory factory) => _factory = factory;

    private async Task EnsurePasswordAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == username);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await users.ResetPasswordAsync(user, token, TestSecrets.SeedPassword);
        user.MustChangePassword = false;
        user.IsActive = true;
        await users.UpdateAsync(user);
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        await EnsurePasswordAsync(username);
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = TestSecrets.SeedPassword,
            rememberMe = false
        });
        login.EnsureSuccessStatusCode();
        var json = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("data").GetProperty("token").GetString());
        return client;
    }

    private async Task SetApprovalRequiredAsync(bool required)
    {
        var admin = await LoginAsync("admin");
        var current = await admin.GetAsync("/api/settings");
        current.EnsureSuccessStatusCode();
        var dto = (await current.Content.ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        dto.ApprovalRequired = required;
        dto.WalkInApprovalRequired = required;
        (await admin.PutAsJsonAsync("/api/settings", dto)).EnsureSuccessStatusCode();
    }

    private async Task<(Guid DepartmentId, Guid HostId, Guid PurposeId)> SeedRefsAsync()
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

    private async Task<HttpResponseMessage> RegisterWithPhotoAsync(HttpClient client, string name, string? photoBase64)
    {
        await SetApprovalRequiredAsync(false);
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        return await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            companyName = "Face Gate Co",
            telephone = "9000000088",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            photoBase64
        });
    }

    [Fact]
    public void Stub_Counts_Zero_One_Two_And_Three_Faces_By_Fixture_Size()
    {
        var stub = new StubFaceEmbeddingService();
        Assert.Equal(0, stub.CountFaces(Convert.FromBase64String(TestPhotos.NoFaceBase64())));
        Assert.Equal(1, stub.CountFaces(Convert.FromBase64String(TestPhotos.Base64("one"))));
        Assert.Equal(2, stub.CountFaces(Convert.FromBase64String(TestPhotos.TwoFacesBase64())));
        Assert.Equal(3, stub.CountFaces(Convert.FromBase64String(TestPhotos.ThreeFacesBase64())));
    }

    [Fact]
    public async Task ValidatePhoto_Rejects_Zero_Faces()
    {
        var client = await LoginAsync("reception");
        var response = await client.PostAsJsonAsync("/api/visitors/validate-photo", new
        {
            photoBase64 = TestPhotos.NoFaceBase64()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(FacePhotoRules.NoFaceMessage, body);
        Assert.Contains(FacePhotoRules.ErrorCodeNoFace, body);
    }

    [Fact]
    public async Task ValidatePhoto_Accepts_Exactly_One_Face()
    {
        var client = await LoginAsync("reception");
        var response = await client.PostAsJsonAsync("/api/visitors/validate-photo", new
        {
            photoBase64 = TestPhotos.Base64("validate-one")
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(1, json.GetProperty("data").GetProperty("faceCount").GetInt32());
        Assert.True(json.GetProperty("data").GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task ValidatePhoto_Rejects_Two_Faces()
    {
        var client = await LoginAsync("reception");
        var response = await client.PostAsJsonAsync("/api/visitors/validate-photo", new
        {
            photoBase64 = TestPhotos.TwoFacesBase64()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(FacePhotoRules.MultipleFacesMessage, body);
        Assert.Contains(FacePhotoRules.ErrorCodeMultipleFaces, body);
    }

    [Fact]
    public async Task ValidatePhoto_Rejects_Three_Or_More_Faces()
    {
        var client = await LoginAsync("reception");
        var response = await client.PostAsJsonAsync("/api/visitors/validate-photo", new
        {
            photoBase64 = TestPhotos.ThreeFacesBase64()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FacePhotoRules.MultipleFacesMessage, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_With_MultiFace_Photo_Is_Rejected_And_Not_Stored()
    {
        var client = await LoginAsync("reception");
        var beforeCount = await CountVisitorPhotosAsync();

        var response = await RegisterWithPhotoAsync(client, "Multi Face Reject", TestPhotos.TwoFacesBase64());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FacePhotoRules.MultipleFacesMessage, await response.Content.ReadAsStringAsync());

        Assert.Equal(beforeCount, await CountVisitorPhotosAsync());
    }

    [Fact]
    public async Task Register_With_Zero_Face_Photo_Is_Rejected()
    {
        var client = await LoginAsync("reception");
        var response = await RegisterWithPhotoAsync(client, "Zero Face Reject", TestPhotos.NoFaceBase64());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FacePhotoRules.NoFaceMessage, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_With_One_Face_Photo_Succeeds()
    {
        var client = await LoginAsync("reception");
        var response = await RegisterWithPhotoAsync(client, "Single Face Accept", TestPhotos.Base64("register-one"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("data").GetProperty("photoUrl").GetString()));
    }

    [Fact]
    public async Task Backend_Cannot_Be_Bypassed_By_Skipping_ValidatePhoto()
    {
        var client = await LoginAsync("reception");
        var response = await RegisterWithPhotoAsync(client, "Bypass Attempt", TestPhotos.ThreeFacesBase64());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Multiple faces", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [RequiresFaceModelsFact]
    public void Scrfd_Detects_Single_Synthetic_Portrait()
    {
        using var detector = FaceTestAssets.Detector();
        var faces = detector.Detect(RgbImage.Decode(FaceTestAssets.Image("face-a.jpg")));
        Assert.Single(faces);
    }

    [RequiresFaceModelsFact]
    public void Scrfd_Detects_Two_Faces_In_Side_By_Side_Composite()
    {
        using var detector = FaceTestAssets.Detector();
        var composite = CompositeSideBySide(
            FaceTestAssets.Image("face-a.jpg"),
            FaceTestAssets.Image("face-b.jpg"));
        var faces = detector.Detect(RgbImage.Decode(composite));
        Assert.True(faces.Count >= 2, $"Expected ≥2 faces, got {faces.Count}.");
    }

    [RequiresFaceModelsFact]
    public void InsightFace_EnsureExactlyOneFace_Rejects_Composite()
    {
        using var service = CreateInsightFaceService();
        var composite = CompositeSideBySide(
            FaceTestAssets.Image("face-a.jpg"),
            FaceTestAssets.Image("face-b.jpg"));

        var ex = Assert.Throws<InvalidOperationException>(() => service.EnsureExactlyOneFace(composite));
        Assert.Equal(FacePhotoRules.MultipleFacesMessage, ex.Message);
    }

    [RequiresFaceModelsFact]
    public void InsightFace_EnsureExactlyOneFace_Accepts_Single_Portrait()
    {
        using var service = CreateInsightFaceService();
        service.EnsureExactlyOneFace(FaceTestAssets.Image("face-a.jpg"));
    }

    private static InsightFaceService CreateInsightFaceService()
    {
        var options = Options.Create(new FaceRecognitionOptions
        {
            Enabled = true,
            ModelDirectory = FaceTestAssets.ModelDirectory!
        });
        var env = new FakeWebHostEnvironment { ContentRootPath = FaceTestAssets.ModelDirectory! };
        return new InsightFaceService(options, env, NullLogger<InsightFaceService>.Instance);
    }

    private async Task<int> CountVisitorPhotosAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.VisitorPhotos.IgnoreQueryFilters()
            .CountAsync(p => p.Visitor.TenantId == WellKnownTenants.TiaanoId);
    }

    private static byte[] CompositeSideBySide(byte[] leftJpeg, byte[] rightJpeg)
    {
        using var left = Image.Load<Rgb24>(leftJpeg);
        using var right = Image.Load<Rgb24>(rightJpeg);
        var height = Math.Max(left.Height, right.Height);
        using var canvas = new Image<Rgb24>(left.Width + right.Width, height);
        canvas.Mutate(ctx =>
        {
            ctx.DrawImage(left, new Point(0, 0), 1f);
            ctx.DrawImage(right, new Point(left.Width, 0), 1f);
        });
        using var ms = new MemoryStream();
        canvas.Save(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
