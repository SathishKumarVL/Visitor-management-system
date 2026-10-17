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
        var admin = await LoginAsync("superadmin");
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
            gender = 1,
            companyName = "Lifecycle Test Co",
            telephone = "9000000001",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = walkIn,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            // A fresh face per call: a visitor left inside by an earlier run must not be face-matched.
            photoBase64 = TestPhotos.Base64($"{name}-{Guid.NewGuid():N}"),
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("data");
    }

    private static Guid VisitId(JsonElement visit) => visit.GetProperty("visitId").GetGuid();

    /// <summary>Check-out requires the visitor's feedback when questions are configured.</summary>
    private static async Task SubmitFeedbackAsync(HttpClient client, Guid visitId)
    {
        var questions = await client.GetFromJsonAsync<JsonElement>("/api/masters/feedback-questions?activeOnly=true", JsonOptions);
        var answers = questions.GetProperty("data").EnumerateArray()
            .Where(q => !q.TryGetProperty("locationId", out var loc) || loc.ValueKind == JsonValueKind.Null)
            .Select(q => new { questionId = q.GetProperty("id").GetGuid(), rating = 5 })
            .ToArray();
        if (answers.Length == 0) return;
        var response = await client.PostAsJsonAsync($"/api/visitors/{visitId}/feedback", new { answers });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

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
        await SubmitFeedbackAsync(reception, visitId);

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
        await SubmitFeedbackAsync(reception, visitId);
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

            var admin = await LoginAsync("superadmin");
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

            var admin = await LoginAsync("superadmin");
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

            var admin = await LoginAsync("superadmin");
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

            var admin = await LoginAsync("superadmin");
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
        await SubmitFeedbackAsync(reception, visitId);
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { })).EnsureSuccessStatusCode();

        var detail = await reception.GetAsync($"/api/visitors/{visitId}");
        detail.EnsureSuccessStatusCode();
        var json = await detail.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(original, json.GetProperty("data").GetProperty("visitNumber").GetString());
    }

    // ---------------------------------------------------------- expected visit

    [Theory]
    [InlineData(-1440)]
    [InlineData(-30)]
    public async Task Expected_Visit_Cannot_Be_Booked_In_The_Past(int minutesFromNow)
    {
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var slot = AppTime.Now.AddMinutes(minutesFromNow);

        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "Past Appointment",
            gender = 1,
            companyName = "Preregistered Co",
            phone = "9000000003",
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("cannot be in the past", await create.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expected_Visit_Requires_A_Purpose_And_Keeps_The_Others_Reason()
    {
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, _) = await SeedRefsAsync();
        var slot = AppTime.Now.AddHours(2);
        object Body(string? purposeNotes) => new
        {
            visitorName = "Purpose Check",
            gender = 2,
            companyName = "Preregistered Co",
            phone = $"9{Random.Shared.Next(100_000_000, 999_999_999)}",
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = Array.Empty<Guid>(),
            purposeNotes,
            locationIds = Array.Empty<Guid>()
        };

        var missing = await reception.PostAsJsonAsync("/api/visitors/expected", Body(null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Purpose of visit is required", await missing.Content.ReadAsStringAsync());

        var others = await reception.PostAsJsonAsync("/api/visitors/expected", Body("  Annual vendor audit  "));
        Assert.True(others.IsSuccessStatusCode, await others.Content.ReadAsStringAsync());
        var data = (await others.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal("Annual vendor audit", data.GetProperty("purposeNotes").GetString());
        Assert.Contains(data.GetProperty("purposes").EnumerateArray(), p => p.GetString() == "Others");
    }

    [Fact]
    public async Task Expected_Visit_Is_Superseded_Not_Deleted_On_Arrival()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var slot = AppTime.Now.AddHours(1);
        var phone = $"9{Random.Shared.Next(100_000_000, 999_999_999)}";

        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "Expected Then Arrived",
            gender = 1,
            companyName = "Preregistered Co",
            phone,
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
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
            gender = 1,
            companyName = "Preregistered Co",
            telephone = phone,
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = false,
            expectedVisitId,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(arrival.IsSuccessStatusCode, await arrival.Content.ReadAsStringAsync());

        // The pre-registration must survive as history rather than being erased.
        Assert.Equal(VisitStatus.Cancelled, await StatusOfAsync(expectedVisitId));
    }

    [Fact]
    public async Task Expected_Visit_Requires_A_Mobile_Number()
    {
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var slot = AppTime.Now.AddHours(1);

        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "No Mobile",
            gender = 1,
            companyName = "Preregistered Co",
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("mobile number is required", await create.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Booking_Reuses_The_Visitor_With_That_Mobile_And_Blocks_A_Duplicate_Appointment()
    {
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var phone = $"9{Random.Shared.Next(100_000_000, 999_999_999)}";
        var slot = AppTime.Now.AddDays(1);
        object Booking(string time) => new
        {
            visitorName = "Repeat Booker",
            gender = 1,
            companyName = "Repeat Co",
            phone,
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = time,
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        };

        var first = await reception.PostAsJsonAsync("/api/visitors/expected", Booking("10:00"));
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var firstData = (await first.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");

        var lookup = await reception.GetFromJsonAsync<JsonElement>($"/api/visitors/lookup?phone={phone}", JsonOptions);
        var found = lookup.GetProperty("data");
        Assert.Equal(firstData.GetProperty("visitorId").GetGuid(), found.GetProperty("visitorId").GetGuid());
        Assert.Equal("Repeat Booker", found.GetProperty("visitorName").GetString());
        Assert.Single(found.GetProperty("openBookings").EnumerateArray());

        var duplicate = await reception.PostAsJsonAsync("/api/visitors/expected", Booking("15:00"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("already has an appointment", await duplicate.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Visitors.IgnoreQueryFilters().CountAsync(v => v.Phone == phone));

        var bookings = await db.VisitorVisits.IgnoreQueryFilters().Where(v => v.Visitor.Phone == phone).ToListAsync();
        foreach (var b in bookings) b.Status = VisitStatus.Cancelled;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Walk_In_Registration_Finds_And_Closes_Todays_Booking(bool deskSaidNotThisVisitor)
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var (departmentId, hostId, purposeId) = await SeedRefsAsync();
        var phone = $"9{Random.Shared.Next(100_000_000, 999_999_999)}";
        var slot = AppTime.Now.AddMinutes(30);
        if (slot.Date != AppTime.Now.Date) return;

        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "Booked Walk Up",
            gender = 2,
            companyName = "Preregistered Co",
            phone,
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>()
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var bookingId = VisitId((await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data"));

        var match = await reception.GetFromJsonAsync<JsonElement>($"/api/visitors/expected/match?phone=%2B91{phone}", JsonOptions);
        var found = match.GetProperty("data").EnumerateArray().Single(m => m.GetProperty("visitId").GetGuid() == bookingId);
        Assert.Equal(hostId, found.GetProperty("hostEmployeeId").GetGuid());
        Assert.Contains(purposeId, found.GetProperty("purposeIds").EnumerateArray().Select(p => p.GetGuid()));

        var walkIn = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = "Booked Walk Up",
            gender = 2,
            companyName = "Preregistered Co",
            telephone = phone,
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            skipBookingMatch = deskSaidNotThisVisitor,
            idTypeName = "Aadhaar",
            idNumber = "123456789012"
        });
        Assert.True(walkIn.IsSuccessStatusCode, await walkIn.Content.ReadAsStringAsync());

        Assert.Equal(
            deskSaidNotThisVisitor ? VisitStatus.Expected : VisitStatus.Cancelled,
            await StatusOfAsync(bookingId));
        if (!deskSaidNotThisVisitor)
        {
            var after = await reception.GetFromJsonAsync<JsonElement>($"/api/visitors/expected/match?phone={phone}", JsonOptions);
            Assert.DoesNotContain(after.GetProperty("data").EnumerateArray(), m => m.GetProperty("visitId").GetGuid() == bookingId);
        }
        else
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leftover = await db.VisitorVisits.IgnoreQueryFilters().FirstAsync(v => v.Id == bookingId);
            leftover.Status = VisitStatus.Cancelled;
            await db.SaveChangesAsync();
        }
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

    // -------------------------------------------------------- emergency mode

    [Fact]
    public async Task Emergency_Roster_Counts_People_Inside_And_Does_Not_Claim_Employee_Tracking()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Roster Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var security = await LoginAsync("security");
        var response = await security.GetAsync("/api/emergency/roster");
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");

        Assert.True(data.GetProperty("totalInside").GetInt32() >= 1);
        Assert.False(data.GetProperty("employeeTrackingAvailable").GetBoolean());
        Assert.Contains(data.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("visitId").GetGuid() == visitId);
    }

    [Fact]
    public async Task Emergency_RollCall_Is_Persisted_And_Leaves_The_Visit_Untouched()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Roll Call Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var security = await LoginAsync("security");
        var mark = await security.PostAsJsonAsync("/api/emergency/roll-call", new
        {
            visitId,
            status = (int)EmergencyRollCallStatus.Evacuated
        });
        Assert.True(mark.IsSuccessStatusCode, await mark.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var events = await db.EmergencyRollCallEvents.IgnoreQueryFilters()
            .Where(e => e.VisitorVisitId == visitId)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal(EmergencyRollCallStatus.Evacuated, events[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(events[0].RecordedByUserId));

        // The emergency mark is an event, not a change to the visit's own lifecycle state.
        Assert.Equal(VisitStatus.Inside, await StatusOfAsync(visitId));
    }

    [Fact]
    public async Task Emergency_RollCall_Keeps_Every_Mark_And_Reports_The_Latest()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Remarked Visitor"));
        (await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { })).EnsureSuccessStatusCode();

        var security = await LoginAsync("security");
        foreach (var status in new[] { EmergencyRollCallStatus.Missing, EmergencyRollCallStatus.Verified })
        {
            var response = await security.PostAsJsonAsync("/api/emergency/roll-call", new { visitId, status = (int)status });
            response.EnsureSuccessStatusCode();
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await db.EmergencyRollCallEvents.IgnoreQueryFilters()
            .Where(e => e.VisitorVisitId == visitId)
            .ToListAsync();

        // Corrections append rather than overwrite, so the roll call stays auditable.
        Assert.Equal(2, events.Count);

        var roster = await security.GetAsync("/api/emergency/roster");
        roster.EnsureSuccessStatusCode();
        var item = (await roster.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data").GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("visitId").GetGuid() == visitId);
        Assert.Equal((int)EmergencyRollCallStatus.Verified, item.GetProperty("rollCallStatus").GetInt32());
    }

    [Fact]
    public async Task Emergency_RollCall_Rejects_A_Visitor_Who_Is_Not_Inside()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var visitId = VisitId(await RegisterAsync(reception, "Not Inside Roll Call Visitor"));

        var security = await LoginAsync("security");
        var response = await security.PostAsJsonAsync("/api/emergency/roll-call", new
        {
            visitId,
            status = (int)EmergencyRollCallStatus.Verified
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Emergency_Roster_Requires_An_Operational_Role()
    {
        var host = await LoginAsync("host");
        var response = await host.GetAsync("/api/emergency/roster");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
