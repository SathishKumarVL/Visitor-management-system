using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Optimistic concurrency for Visit lifecycle: SQL Server rowversion + controlled HTTP 409.
/// </summary>
[Collection("CoreWorkflow")]
public class VisitConcurrencyTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public VisitConcurrencyTests(TestApiFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string? password = null)
    {
        password ??= TestSecrets.SeedPassword;
        await EnsureSeedPasswordAsync(username, password);
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password,
            rememberMe = false
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task EnsureSeedPasswordAsync(string username, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
        Assert.NotNull(user);
        var token = await users.GeneratePasswordResetTokenAsync(user!);
        await users.ResetPasswordAsync(user!, token, password);
        user!.MustChangePassword = false;
        user.IsActive = true;
        await users.UpdateAsync(user);
    }

    private async Task SetApprovalRequiredAsync(bool required)
    {
        var admin = await LoginAsync("admin");
        var current = await admin.GetAsync("/api/settings");
        current.EnsureSuccessStatusCode();
        var dto = (await current.Content.ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        dto.ApprovalRequired = required;
        dto.WalkInApprovalRequired = required;
        var update = await admin.PutAsJsonAsync("/api/settings", dto);
        update.EnsureSuccessStatusCode();
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

    private async Task<Guid> RegisterApprovedVisitAsync(HttpClient client, string name)
    {
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            companyName = "Concurrency Co",
            telephone = "9000000099",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data").GetProperty("visitId").GetGuid();
    }

    private static ClaimsPrincipal ReceptionPrincipal(string userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "reception"),
            new Claim(ClaimTypes.Role, AppRoles.Reception),
            new Claim(TenantClaims.TenantIdClaim, WellKnownTenants.TiaanoId.ToString())
        ], authenticationType: "Test"));

    private static async Task<(PassDto? Pass, Exception? Error)> RunCheckInAsync(
        IVisitorService service,
        Guid visitId,
        ClaimsPrincipal principal,
        string webRoot)
    {
        try
        {
            var pass = await service.CheckInAsync(visitId, new CheckInRequest(), principal, webRoot);
            return (pass, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    [Fact]
    public async Task CheckIn_Succeeds_For_Approved_Visit()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Happy CheckIn");

        var response = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CheckOut_Succeeds_After_CheckIn()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Happy CheckOut");
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var response = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_CheckOut_When_Not_Inside_Is_Rejected()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Invalid Transition");

        var response = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("500", ((int)response.StatusCode).ToString());
    }

    [Fact]
    public async Task Stale_RowVersion_Save_Throws_DbUpdateConcurrencyException()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Token Entity");

        using var scopeA = _factory.Services.CreateScope();
        scopeA.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tracked = await dbA.VisitorVisits.FirstAsync(v => v.Id == visitId);
        Assert.NotEmpty(tracked.RowVersion);

        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        tracked.Notes = "stale writer";
        tracked.UpdatedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbA.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_CheckIn_One_Succeeds_Other_Is_Controlled_Conflict_Or_Business_Reject()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Dual CheckIn");

        var firstTask = reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        var secondTask = reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        var first = await firstTask;
        var second = await secondTask;

        var codes = new[] { first.StatusCode, second.StatusCode };
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.True(
            codes.Any(c => c is HttpStatusCode.Conflict or HttpStatusCode.BadRequest),
            $"Expected one conflict/business reject, got: {string.Join(',', codes)} / " +
            $"{await first.Content.ReadAsStringAsync()} | {await second.Content.ReadAsStringAsync()}");
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, codes);

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = await db.VisitorVisits.AsNoTracking().FirstAsync(v => v.Id == visitId);
        Assert.Equal(VisitStatus.Inside, visit.Status);
        Assert.Equal(1, await db.VisitorPasses.CountAsync(p => p.VisitorVisitId == visitId && p.IsActive));
    }

    [Fact]
    public async Task Service_Level_Concurrent_CheckIn_Maps_To_Controlled_Failure()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Service Dual");

        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await users.Users.IgnoreQueryFilters().FirstAsync(u => u.UserName == "reception")).Id;
        }

        var principal = ReceptionPrincipal(userId);
        var webRoot = Path.GetTempPath();

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        scope1.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        scope2.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var svc1 = scope1.ServiceProvider.GetRequiredService<IVisitorService>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<IVisitorService>();

        var outcomes = await Task.WhenAll(
            RunCheckInAsync(svc1, visitId, principal, webRoot),
            RunCheckInAsync(svc2, visitId, principal, webRoot));

        Assert.Equal(1, outcomes.Count(o => o.Error is null));
        var loser = outcomes.Single(o => o.Error is not null).Error!;
        Assert.True(
            loser is ConcurrencyConflictException or InvalidOperationException,
            $"Unexpected loser exception: {loser.GetType().Name}: {loser.Message}");
    }

    [Fact]
    public async Task Http_CheckIn_Conflict_Returns_409_With_Safe_Message()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Http 409 Message");

        // Drive the race until we observe either Conflict or business reject — never 500 / SQL internals.
        HttpResponseMessage? loser = null;
        for (var attempt = 0; attempt < 8 && loser is null; attempt++)
        {
            var id = attempt == 0
                ? visitId
                : await RegisterApprovedVisitAsync(reception, $"Concurrency Http 409 Message {attempt}");

            var firstTask = reception.PostAsJsonAsync($"/api/visitors/{id}/check-in", new { });
            var secondTask = reception.PostAsJsonAsync($"/api/visitors/{id}/check-in", new { });
            var first = await firstTask;
            var second = await secondTask;

            var pair = new[] { first, second };
            Assert.DoesNotContain(pair, r => r.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Contains(pair, r => r.IsSuccessStatusCode);

            loser = pair.FirstOrDefault(r =>
                r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        }

        Assert.NotNull(loser);
        var body = await loser!.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DbUpdate", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE VisitorVisits", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rowversion", body, StringComparison.OrdinalIgnoreCase);
        if (loser.StatusCode == HttpStatusCode.Conflict)
            Assert.Contains("modified by another user", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_RowVersion_Rejects_Second_Writer_At_Database()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Forced Stale");
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        using var scopeA = _factory.Services.CreateScope();
        scopeA.ServiceProvider.GetRequiredService<ITenantContext>().Set(WellKnownTenants.TiaanoId);
        var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stale = await dbA.VisitorVisits.Include(v => v.Passes).FirstAsync(v => v.Id == visitId);
        var staleVersion = stale.RowVersion.ToArray();

        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { })).EnsureSuccessStatusCode();

        dbA.Entry(stale).Property(v => v.RowVersion).OriginalValue = staleVersion;
        stale.Status = VisitStatus.Inside;
        stale.CheckOutAt = null;
        stale.UpdatedAt = DateTime.UtcNow;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbA.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_CheckOut_Never_Returns_Unhandled_500()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = await RegisterApprovedVisitAsync(reception, "Concurrency Dual CheckOut");
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var firstTask = reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        var secondTask = reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        var first = await firstTask;
        var second = await secondTask;

        var codes = new[] { first.StatusCode, second.StatusCode };
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.Contains(codes, c => c is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, codes);

        var loserBody = codes[0] == HttpStatusCode.OK
            ? await second.Content.ReadAsStringAsync()
            : await first.Content.ReadAsStringAsync();
        if (codes.Contains(HttpStatusCode.Conflict))
            Assert.Contains("modified by another user", loserBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantA_Cannot_CheckIn_TenantB_Visit()
    {
        // Reuse Tenant B fixture visit from isolation suite seed pattern.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.Id == TenantBId))
            {
                // Minimal tenant B so the negative path has a foreign visit id.
                db.Tenants.Add(new Tenant
                {
                    Id = TenantBId,
                    Name = "Tenant B",
                    Code = "TENANTB",
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            if (!await db.VisitorVisits.IgnoreQueryFilters().AnyAsync(v => v.TenantId == TenantBId))
            {
                var visitor = new Visitor
                {
                    TenantId = TenantBId,
                    VisitorNumber = $"TB-V-{Guid.NewGuid():N}"[..20],
                    FullName = "Secret Visitor B",
                    CompanyName = "OtherCorp",
                    CreatedBy = "test"
                };
                db.Visitors.Add(visitor);

                var dept = await db.Departments.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(d => d.TenantId == TenantBId);
                if (dept is null)
                {
                    dept = new Department
                    {
                        TenantId = TenantBId,
                        Name = "Admin B",
                        Code = "ADMIN",
                        SortOrder = 1,
                        CreatedBy = "test"
                    };
                    db.Departments.Add(dept);
                    await db.SaveChangesAsync();
                }

                var host = await db.Employees.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(e => e.TenantId == TenantBId);
                if (host is null)
                {
                    host = new Employee
                    {
                        TenantId = TenantBId,
                        FullName = "Host B",
                        Email = "hostb@example.test",
                        DepartmentId = dept.Id,
                        CreatedBy = "test"
                    };
                    db.Employees.Add(host);
                    await db.SaveChangesAsync();
                }

                db.VisitorVisits.Add(new VisitorVisit
                {
                    TenantId = TenantBId,
                    VisitorId = visitor.Id,
                    VisitNumber = $"TB-2026-{Random.Shared.Next(100000, 999999)}",
                    Status = VisitStatus.Approved,
                    VisitDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    VisitTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                    DepartmentId = dept.Id,
                    HostEmployeeId = host.Id,
                    CreatedBy = "test"
                });
                await db.SaveChangesAsync();
            }
        }

        Guid tenantBVisitId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tenantBVisitId = await db.VisitorVisits.IgnoreQueryFilters()
                .Where(v => v.TenantId == TenantBId)
                .Select(v => v.Id)
                .FirstAsync();
        }

        var receptionA = await LoginAsync("reception");
        var response = await receptionA.PostAsJsonAsync($"/api/visitors/{tenantBVisitId}/check-in", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not found", body, StringComparison.OrdinalIgnoreCase);
    }
}
