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
/// Proves the Sites write contract the SPA uses (PUT /api/sites/{id}) is registered and
/// tenant-/role-scoped. A production 405 with Allow: GET, HEAD, OPTIONS, TRACE is IIS WebDAV,
/// not a missing ASP.NET Core action — these tests fail closed if the PUT route ever disappears.
/// </summary>
public class SitesCrudContractTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Same Tenant B id used by TenantIsolationTests (distinct from Site row ids).</summary>
    private static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HeadquartersSiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ReceptionUser = "sites-crud-reception";
    private const string ReceptionPassword = "SitesCrud_Reception1!";

    public SitesCrudContractTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Put_Sites_Id_Updates_Site_For_Admin()
    {
        var siteId = await EnsureEditableSiteAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            name = $"Editable Site {suffix}",
            code = "SCRUD",
            address = "Updated address",
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal($"Editable Site {suffix}", body.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Updated address", body.GetProperty("data").GetProperty("address").GetString());
    }

    [Fact]
    public async Task Put_Sites_Id_Rejects_Reception()
    {
        var siteId = await EnsureEditableSiteAsync();
        await EnsureReceptionAsync();
        var client = await LoginAsync(ReceptionUser, ReceptionPassword);

        var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            name = "Should Not Persist",
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Sites_Id_Unknown_Returns_BadRequest()
    {
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var missing = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var response = await client.PutAsJsonAsync($"/api/sites/{missing}", new
        {
            name = "Ghost Site",
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_Sites_Id_Empty_Name_Is_Rejected()
    {
        var siteId = await EnsureEditableSiteAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            name = "   ",
            isActive = true,
            isDefault = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Cannot_Update_Other_Tenant_Site()
    {
        var foreignSiteId = await EnsureTenantBSiteAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);

        var response = await client.PutAsJsonAsync($"/api/sites/{foreignSiteId}", new
        {
            name = "Cross Tenant Hijack",
            isActive = true,
            isDefault = false
        });

        // Query filter hides the foreign row → same "not found" path as a missing id (no leak).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var foreign = await db.Sites.IgnoreQueryFilters().SingleAsync(s => s.Id == foreignSiteId);
        Assert.NotEqual("Cross Tenant Hijack", foreign.Name);
    }

    [Fact]
    public async Task Put_Setting_Default_Clears_Previous_Default()
    {
        var (primaryId, secondaryId) = await EnsureTwoSitesForDefaultAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);

        // Establish primary as the sole default, then flip to secondary.
        (await client.PutAsJsonAsync($"/api/sites/{primaryId}", new
        {
            name = "Default Swap Primary",
            code = "DSP1",
            isActive = true,
            isDefault = true
        })).EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync($"/api/sites/{secondaryId}", new
        {
            name = "Default Candidate",
            code = "DEF2",
            isActive = true,
            isDefault = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var primary = await db.Sites.IgnoreQueryFilters().SingleAsync(s => s.Id == primaryId);
            var secondary = await db.Sites.IgnoreQueryFilters().SingleAsync(s => s.Id == secondaryId);
            Assert.False(primary.IsDefault);
            Assert.True(secondary.IsDefault);
        }

        // Restore seeded Headquarters as default so sibling fixtures stay stable.
        (await client.PutAsJsonAsync($"/api/sites/{HeadquartersSiteId}", new
        {
            name = "Headquarters",
            code = "HQ",
            isActive = true,
            isDefault = true
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Put_Route_Is_Registered_Not_MethodNotAllowed()
    {
        var siteId = await EnsureEditableSiteAsync();
        var client = await LoginAsync("admin", TestSecrets.SeedPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            name = $"Method Probe {suffix}",
            code = "SCRUD",
            isActive = true,
            isDefault = false
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
                FullName = "Sites CRUD Reception",
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

    private async Task<Guid> EnsureEditableSiteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "admin");
        const string code = "SCRUD";

        var existing = await db.Sites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == admin.TenantId && s.Code == code);
        if (existing is not null)
        {
            existing.Name = "Sites CRUD Editable";
            existing.IsActive = true;
            existing.IsDefault = false;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var site = new Site
        {
            TenantId = admin.TenantId,
            Name = "Sites CRUD Editable",
            Code = code,
            IsActive = true,
            IsDefault = false,
            CreatedBy = "test"
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }

    private async Task<(Guid PrimaryId, Guid SecondaryId)> EnsureTwoSitesForDefaultAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "admin");

        var primary = await UpsertNamedSiteAsync(db, admin.TenantId, "Default Swap Primary", "DSP1", isDefault: false);
        var secondary = await UpsertNamedSiteAsync(db, admin.TenantId, "Default Candidate", "DEF2", isDefault: false);
        return (primary.Id, secondary.Id);
    }

    private static async Task<Site> UpsertNamedSiteAsync(
        ApplicationDbContext db, Guid tenantId, string name, string code, bool isDefault)
    {
        var existing = await db.Sites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Name == name);
        if (existing is not null)
        {
            existing.Code = code;
            existing.IsActive = true;
            existing.IsDefault = isDefault;
            await db.SaveChangesAsync();
            return existing;
        }

        var site = new Site
        {
            TenantId = tenantId,
            Name = name,
            Code = code,
            IsActive = true,
            IsDefault = isDefault,
            CreatedBy = "test"
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site;
    }

    private async Task<Guid> EnsureTenantBSiteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await db.Tenants.AnyAsync(t => t.Id == TenantBId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantBId,
                Code = "TENANTB",
                Name = "Tenant B Isolation",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var existing = await db.Sites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == TenantBId && s.Code == "TB-SITE");
        if (existing is not null)
            return existing.Id;

        var site = new Site
        {
            TenantId = TenantBId,
            Name = "Tenant B HQ",
            Code = "TB-SITE",
            IsActive = true,
            IsDefault = true,
            CreatedBy = "test"
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }
}
