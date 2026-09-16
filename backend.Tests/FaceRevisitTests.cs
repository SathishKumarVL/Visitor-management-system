using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Recognising a returning visitor from their face. Two captures of one person are never bit-identical,
/// so these tests enrol a template and then search with a deliberately drifted copy: the distance is
/// held just outside the old 0.45 cutoff to prove genuine revisits are no longer rejected.
/// </summary>
public class FaceRevisitTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FaceRevisitTests(TestApiFactory factory) => _factory = factory;

    /// <summary>A unit-length template, seeded so each test owns a face nothing else will collide with.</summary>
    private static float[] Template(int seed)
    {
        var rng = new Random(seed);
        var v = new float[FaceRecognition.Dimensions];
        double norm = 0;
        for (var i = 0; i < v.Length; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2 - 1);
            norm += (double)v[i] * v[i];
        }
        norm = Math.Sqrt(norm);
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
        return v;
    }

    /// <summary>Same face, different capture: shifts the template by an exact euclidean distance.</summary>
    private static float[] Drift(float[] source, double distance)
    {
        var step = (float)(distance / Math.Sqrt(source.Length));
        var drifted = new float[source.Length];
        for (var i = 0; i < source.Length; i++) drifted[i] = source[i] + step;
        return drifted;
    }

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
        HttpClient client, string name, float[]? descriptor, Guid? recognizedVisitorId = null)
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
            faceDescriptor = descriptor
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    private async Task<JsonElement> FaceSearchAsync(HttpClient client, float[] descriptor)
    {
        var response = await client.PostAsJsonAsync("/api/visitors/face-search", new { descriptor });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    /// <summary>The regression: a second capture of an enrolled face used to fall outside the cutoff.</summary>
    [Fact]
    public async Task Enrolled_Face_Is_Recognised_When_The_Next_Capture_Drifts()
    {
        var reception = await LoginAsync("reception");
        var enrolled = Template(seed: 8101);

        await RegisterAsync(reception, "Face Drift Visitor", enrolled);

        var match = await FaceSearchAsync(reception, Drift(enrolled, 0.50));

        Assert.Equal(JsonValueKind.Object, match.ValueKind);
        Assert.Equal("Face Drift Visitor", match.GetProperty("visitorName").GetString());
    }

    /// <summary>A different person must still be rejected, so the looser cutoff is not a free pass.</summary>
    [Fact]
    public async Task Different_Face_Is_Still_Rejected()
    {
        var reception = await LoginAsync("reception");
        await RegisterAsync(reception, "Face Baseline Visitor", Template(seed: 8202));

        var stranger = await FaceSearchAsync(reception, Template(seed: 9303));

        Assert.Equal(JsonValueKind.Null, stranger.ValueKind);
    }

    /// <summary>A confirmed match must join the existing visitor's history rather than fork a new record.</summary>
    [Fact]
    public async Task Confirmed_Match_Reuses_The_Visitor_Instead_Of_Duplicating()
    {
        var reception = await LoginAsync("reception");
        var enrolled = Template(seed: 8404);

        await RegisterAsync(reception, "Face Return Visitor", enrolled);
        var match = await FaceSearchAsync(reception, Drift(enrolled, 0.40));
        var visitorId = match.GetProperty("visitorId").GetGuid();

        var second = await RegisterAsync(reception, "Face Return Visitor", enrolled, recognizedVisitorId: visitorId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visits = await db.VisitorVisits.IgnoreQueryFilters()
            .CountAsync(v => v.VisitorId == visitorId);

        Assert.NotEqual(Guid.Empty, second.GetProperty("visitId").GetGuid());
        Assert.Equal(2, visits);
        Assert.Equal(1, await db.Visitors.IgnoreQueryFilters()
            .CountAsync(v => v.Id == visitorId));
    }

    /// <summary>Each visit adds a template, bounded so one visitor cannot grow without limit.</summary>
    [Fact]
    public async Task Repeat_Visits_Accumulate_Templates_Up_To_The_Cap()
    {
        var reception = await LoginAsync("reception");
        var enrolled = Template(seed: 8505);

        var first = await RegisterAsync(reception, "Face Template Visitor", enrolled);
        var visitorId = first.GetProperty("visitorId").GetGuid();

        for (var i = 0; i < FaceRecognition.MaxTemplatesPerVisitor + 2; i++)
            await RegisterAsync(reception, "Face Template Visitor", Drift(enrolled, 0.01 * (i + 1)), visitorId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var templates = await db.VisitorFaceDescriptors.IgnoreQueryFilters()
            .CountAsync(f => f.VisitorId == visitorId);

        Assert.Equal(FaceRecognition.MaxTemplatesPerVisitor, templates);
    }
}
