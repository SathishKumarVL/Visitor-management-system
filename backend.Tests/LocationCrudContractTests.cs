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
/// Proves the Locations write contract the SPA uses (PUT /api/masters/locations/{id}) is registered and
/// tenant-/role-scoped. A production 405 with Allow: GET, HEAD, OPTIONS, TRACE is IIS WebDAV,
/// not a missing ASP.NET Core action — these tests fail closed if the PUT route ever disappears.
/// </summary>
public class LocationCrudContractTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ReceptionUser = "locations-crud-reception";
    private const string ReceptionPassword = "LocationsCrud_Reception1!";
    private const string EditableName = "Locations CRUD Editable";

    public LocationCrudContractTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Put_Locations_Id_Updates_Location_For_Admin()
    {
        var locationId = await EnsureEditableLocationAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{locationId}", new
        {
            name = $"Editable Location {suffix}",
            sortOrder = 3,
            isActive = true,
            requiresPlantNumber = false,
            requiresOtherText = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal($"Editable Location {suffix}", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Put_Locations_Id_Rejects_Reception()
    {
        var locationId = await EnsureEditableLocationAsync();
        await EnsureReceptionAsync();
        var client = await LoginAsync(ReceptionUser, ReceptionPassword);

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{locationId}", new
        {
            name = "Should Not Persist",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Locations_Id_Unknown_Returns_BadRequest()
    {
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var missing = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{missing}", new
        {
            name = "Ghost Location",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_Locations_Id_Empty_Name_Is_Rejected()
    {
        var locationId = await EnsureEditableLocationAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{locationId}", new
        {
            name = "   ",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Cannot_Update_Other_Tenant_Location()
    {
        var foreignLocationId = await EnsureTenantBLocationAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{foreignLocationId}", new
        {
            name = "Cross Tenant Hijack",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var foreign = await db.Locations.IgnoreQueryFilters().SingleAsync(l => l.Id == foreignLocationId);
        Assert.NotEqual("Cross Tenant Hijack", foreign.Name);
    }

    [Fact]
    public async Task Put_Route_Is_Registered_Not_MethodNotAllowed()
    {
        var locationId = await EnsureEditableLocationAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PutAsJsonAsync($"/api/masters/locations/{locationId}", new
        {
            name = $"Method Probe {suffix}",
            isActive = true
        });

        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task EnsureReceptionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeded = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "reception");
        var existing = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == ReceptionUser);
        if (existing is null)
        {
            existing = new ApplicationUser
            {
                UserName = ReceptionUser,
                Email = $"{ReceptionUser}@example.test",
                EmailConfirmed = true,
                FullName = "Locations CRUD Reception",
                TenantId = seeded.TenantId,
                DepartmentId = seeded.DepartmentId,
                IsActive = true,
                MustChangePassword = false
            };
            var created = await users.CreateAsync(existing, ReceptionPassword);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(existing, AppRoles.Reception);
            return;
        }

        existing.IsActive = true;
        existing.MustChangePassword = false;
        await users.UpdateAsync(existing);
        var token = await users.GeneratePasswordResetTokenAsync(existing);
        await users.ResetPasswordAsync(existing, token, ReceptionPassword);
    }

    private async Task<Guid> EnsureEditableLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "admin");

        var existing = await db.Locations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == admin.TenantId && l.Name == EditableName);
        if (existing is not null)
        {
            existing.IsActive = true;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        // Prefer name match after a prior test renamed the row.
        var renamed = await db.Locations.IgnoreQueryFilters()
            .Where(l => l.TenantId == admin.TenantId && l.Name.StartsWith("Editable Location "))
            .OrderByDescending(l => l.UpdatedAt ?? l.CreatedAt)
            .FirstOrDefaultAsync();
        if (renamed is not null)
        {
            renamed.Name = EditableName;
            renamed.IsActive = true;
            await db.SaveChangesAsync();
            return renamed.Id;
        }

        var location = new Location
        {
            TenantId = admin.TenantId,
            Name = EditableName,
            IsActive = true,
            CreatedBy = "test"
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private async Task<Guid> EnsureTenantBLocationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await db.Tenants.AnyAsync(t => t.Id == TenantBId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantBId,
                Name = "Tenant B Isolation",
                Code = "TENB",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var existing = await db.Locations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == TenantBId && l.Name == "Tenant B Only Location");
        if (existing is not null)
            return existing.Id;

        var location = new Location
        {
            TenantId = TenantBId,
            Name = "Tenant B Only Location",
            IsActive = true,
            CreatedBy = "test"
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }
}
