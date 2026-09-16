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
using Tiaano.Vms.Api.Security;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>
/// Covers the visitor lifecycle end to end: REGISTERED -> (PENDING_APPROVAL -> APPROVED) -> INSIDE -> CHECKED_OUT,
/// plus every transition the server must refuse. These run against the real HTTP pipeline so the guards are
/// proven server-side rather than in the wizard.
/// </summary>
[Collection("CoreWorkflow")]
public class CoreWorkflowTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CoreWorkflowTests(TestApiFactory factory) => _factory = factory;

    // ---------------------------------------------------------------- helpers

    private async Task<HttpClient> LoginAsync(string username)
    {
        await EnsureSeedPasswordAsync(username);
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = TestSecrets.SeedPassword,
            rememberMe = false
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task EnsureSeedPasswordAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserName == username);
        Assert.NotNull(user);
        var token = await users.GeneratePasswordResetTokenAsync(user!);
        await users.ResetPasswordAsync(user!, token, TestSecrets.SeedPassword);
        user!.MustChangePassword = false;
        user.IsActive = true;
        await users.UpdateAsync(user);
    }

    /// <summary>Flips tenant approval configuration through the real settings endpoint so the cache is invalidated.</summary>
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

    private async Task<JsonElement> RegisterAsync(HttpClient client, string name, bool walkIn = true)
    {
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var response = await client.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            companyName = "Lifecycle Test Co",
            telephone = "9000000001",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = walkIn
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    private static Guid VisitId(JsonElement visit) => visit.GetProperty("visitId").GetGuid();

    private static string StatusLabel(JsonElement visit) => visit.GetProperty("statusLabel").GetString() ?? "";

    private async Task<VisitStatus> StatusOfAsync(Guid visitId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.VisitorVisits.IgnoreQueryFilters()
            .Where(v => v.Id == visitId)
            .Select(v => v.Status)
            .FirstAsync();
    }

    // ------------------------------------------------- approval-disabled flow

    [Fact]
    public async Task Registration_Without_Approval_Lands_Approved_And_Checks_In()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");

        var visit = await RegisterAsync(reception, "No Approval Visitor");
        var visitId = VisitId(visit);
        Assert.Equal(VisitStatus.Approved, await StatusOfAsync(visitId));

        var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
        Assert.Equal(VisitStatus.Inside, await StatusOfAsync(visitId));
    }

    [Fact]
    public async Task Duplicate_CheckIn_Is_Rejected()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Double CheckIn Visitor"));

        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();
        var second = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("already checked in", await second.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(VisitStatus.Inside, await StatusOfAsync(visitId));
    }

    [Fact]
    public async Task CheckOut_Then_Duplicate_CheckOut_Is_Rejected()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Checkout Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var first = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Equal(VisitStatus.CheckedOut, await StatusOfAsync(visitId));

        var second = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("ALREADY CHECKED OUT", await second.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckOut_Rejected_When_Visitor_Is_Not_Inside()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Never Entered Visitor"));

        var response = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not currently inside", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(VisitStatus.Approved, await StatusOfAsync(visitId));
    }

    [Fact]
    public async Task CheckedOut_Visitor_Cannot_Be_Checked_In_Again()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Reentry Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { })).EnsureSuccessStatusCode();

        var response = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(VisitStatus.CheckedOut, await StatusOfAsync(visitId));
    }

    // -------------------------------------------------- approval-enabled flow

    [Fact]
    public async Task Registration_With_Approval_Parks_Visit_And_Blocks_CheckIn()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visit = await RegisterAsync(reception, "Pending Approval Visitor");
            var visitId = VisitId(visit);

            Assert.Equal(VisitStatus.PendingApproval, await StatusOfAsync(visitId));
            Assert.Contains("pending", StatusLabel(visit), StringComparison.OrdinalIgnoreCase);

            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.Equal(HttpStatusCode.BadRequest, checkIn.StatusCode);
            Assert.Contains("approval", await checkIn.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(VisitStatus.PendingApproval, await StatusOfAsync(visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    [Fact]
    public async Task Approved_Visit_Becomes_CheckInable()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "To Be Approved Visitor"));

            var admin = await LoginAsync("admin");
            var approve = await admin.PostAsJsonAsync($"/api/approvals/{visitId}/approve", new { });
            Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
            Assert.Equal(VisitStatus.Approved, await StatusOfAsync(visitId));

            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
            Assert.Equal(VisitStatus.Inside, await StatusOfAsync(visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    [Fact]
    public async Task Rejected_Visit_Cannot_Be_Checked_In()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "Rejected Visitor"));

            var admin = await LoginAsync("admin");
            var reject = await admin.PostAsJsonAsync($"/api/approvals/{visitId}/reject", new { reason = "Not expected today" });
            Assert.True(reject.IsSuccessStatusCode, await reject.Content.ReadAsStringAsync());
            Assert.Equal(VisitStatus.Rejected, await StatusOfAsync(visitId));

            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.Equal(HttpStatusCode.BadRequest, checkIn.StatusCode);
            Assert.Contains("rejected", await checkIn.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(VisitStatus.Rejected, await StatusOfAsync(visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    [Fact]
    public async Task Rejection_Requires_A_Reason()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "Reasonless Rejection Visitor"));

            var admin = await LoginAsync("admin");
            var reject = await admin.PostAsJsonAsync($"/api/approvals/{visitId}/reject", new { reason = "" });

            Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);
            Assert.Equal(VisitStatus.PendingApproval, await StatusOfAsync(visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    [Fact]
    public async Task Approval_Decision_Records_Who_And_When()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "Audited Approval Visitor"));

            var admin = await LoginAsync("admin");
            (await admin.PostAsJsonAsync($"/api/approvals/{visitId}/reject", new { reason = "Site closed" })).EnsureSuccessStatusCode();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var approval = await db.Approvals.IgnoreQueryFilters()
                .FirstAsync(a => a.VisitorVisitId == visitId);

            Assert.Equal(ApprovalStatus.Rejected, approval.Status);
            Assert.Equal("Site closed", approval.RejectionReason);
            Assert.False(string.IsNullOrWhiteSpace(approval.ActionByUserId));
            Assert.NotNull(approval.ActionAt);
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    [Fact]
    public async Task Security_Role_Cannot_Approve()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "Security Cannot Approve Visitor"));

            var security = await LoginAsync("security");
            var approve = await security.PostAsJsonAsync($"/api/approvals/{visitId}/approve", new { });

            Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
            Assert.Equal(VisitStatus.PendingApproval, await StatusOfAsync(visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    // ------------------------------------------------------------ visit number

    [Fact]
    public async Task Visit_Number_Is_Human_Readable_And_Hides_Database_Ids()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visit = await RegisterAsync(reception, "Numbered Visitor");

        var visitNumber = visit.GetProperty("visitNumber").GetString()!;
        Assert.Matches(@"^[A-Z]+-\d{4}-\d{6}$", visitNumber);
        Assert.DoesNotContain(VisitId(visit).ToString(), visitNumber);
    }

    [Fact]
    public async Task Concurrent_Registrations_Produce_Unique_Visit_Numbers()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");

        var registrations = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => RegisterAsync(reception, $"Concurrent Visitor {i}")));

        var numbers = registrations.Select(r => r.GetProperty("visitNumber").GetString()!).ToList();
        Assert.Equal(numbers.Count, numbers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Visit_Number_Is_Stable_Across_The_Lifecycle()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var registered = await RegisterAsync(reception, "Stable Number Visitor");
        var visitId = VisitId(registered);
        var original = registered.GetProperty("visitNumber").GetString();

        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { })).EnsureSuccessStatusCode();

        var detail = await reception.GetAsync($"/api/visitors/{visitId}");
        detail.EnsureSuccessStatusCode();
        var json = await detail.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(original, json.GetProperty("data").GetProperty("visitNumber").GetString());
    }

    // ---------------------------------------------------------- expected visit

    [Fact]
    public async Task Expected_Visit_Is_Superseded_Not_Deleted_On_Arrival()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();

        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "Expected Then Arrived",
            companyName = "Preregistered Co",
            phone = "9000000002",
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            expectedTime = "10:00",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var expected = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var expectedVisitId = VisitId(expected);
        Assert.Equal(VisitStatus.Expected, await StatusOfAsync(expectedVisitId));

        var arrival = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = "Expected Then Arrived",
            companyName = "Preregistered Co",
            telephone = "9000000002",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = false,
            expectedVisitId
        });
        Assert.True(arrival.IsSuccessStatusCode, await arrival.Content.ReadAsStringAsync());

        // The pre-registration must survive as history rather than being erased.
        Assert.Equal(VisitStatus.Cancelled, await StatusOfAsync(expectedVisitId));
    }

    [Fact]
    public async Task Cancelled_Visit_Cannot_Be_Checked_In()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Cancelled Visitor"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visit = await db.VisitorVisits.IgnoreQueryFilters().FirstAsync(v => v.Id == visitId);
            visit.Status = VisitStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        Assert.Equal(HttpStatusCode.BadRequest, checkIn.StatusCode);
        Assert.Contains("cancelled", await checkIn.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------- pass

    [Fact]
    public async Task Pass_Is_Issued_On_CheckIn_And_Carries_No_Qr_Payload()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Pass Visitor"));

        var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
        checkIn.EnsureSuccessStatusCode();
        var body = await checkIn.Content.ReadAsStringAsync();

        Assert.DoesNotContain("qr", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("barcode", body, StringComparison.OrdinalIgnoreCase);

        var pass = await reception.GetAsync($"/api/pass/{visitId}");
        pass.EnsureSuccessStatusCode();
        var passJson = (await pass.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(passJson.GetProperty("visitNumber").GetString()));
    }

    [Fact]
    public async Task No_Pass_Is_Issued_While_A_Visit_Awaits_Approval()
    {
        await SetApprovalRequiredAsync(true);
        try
        {
            var reception = await LoginAsync("reception");
            var visitId = VisitId(await RegisterAsync(reception, "Unpassed Pending Visitor"));

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.VisitorPasses.IgnoreQueryFilters().AnyAsync(p => p.VisitorVisitId == visitId));
        }
        finally
        {
            await SetApprovalRequiredAsync(false);
        }
    }

    // ------------------------------------------------------------ audit trail

    [Fact]
    public async Task CheckIn_And_CheckOut_Are_Audited()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Audited Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityId == visitId.ToString())
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains("VisitorCheckedIn", actions);
        Assert.Contains("VisitorCheckedOut", actions);
    }
}

/// <summary>
/// Phase 15 — proves the relaxed login limiter is reachable only from the two named local
/// environments and can never be inherited by production or any unrecognised environment.
/// </summary>
public class RateLimitPolicyTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Named_Local_Environments_Are_Relaxed(string environmentName) =>
        Assert.True(RateLimitPolicy.IsLoginRateLimitRelaxed(environmentName));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    [InlineData("Development ")]
    [InlineData("Development,Production")]
    [InlineData("Testing1")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_Else_Stays_Rate_Limited(string? environmentName) =>
        Assert.False(RateLimitPolicy.IsLoginRateLimitRelaxed(environmentName));

    [Fact]
    public void Production_Login_Limit_Is_Bounded()
    {
        Assert.True(RateLimitPolicy.LoginPermitLimit is > 0 and <= 20);
        Assert.Equal(0, RateLimitPolicy.LoginQueueLimit);
        Assert.True(RateLimitPolicy.LoginWindow <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Program_Uses_The_Shared_Policy_Rather_Than_An_Inline_Environment_Check()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "backend", "Program.cs"));
        var text = File.ReadAllText(path);

        Assert.Contains("RateLimitPolicy.IsLoginRateLimitRelaxed", text);
        Assert.DoesNotContain("IsEnvironment(\"Testing\")", text);
    }
}
