using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.DTOs;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Check-out email waits for minutes of meeting; check-out itself does not.</summary>
[Collection("CoreWorkflow")]
public class CheckOutMailAfterMomTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public CheckOutMailAfterMomTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVisitMailService>();
            services.AddSingleton<IVisitMailService>(_mail);
        }));
    }

    [Fact]
    public async Task CheckOut_Without_Mom_Defers_Email_Until_Minutes_Are_Saved()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Deferred Mail");

        var superadmin = await LoginAsync("superadmin");
        var checkOut = await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true });
        Assert.True(checkOut.IsSuccessStatusCode, await checkOut.Content.ReadAsStringAsync());

        await Task.Delay(1500);
        Assert.Empty(_mail.CheckOutsFor(visitNumber));

        var admin = await LoginAsync("admin");
        var save = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new { meetingMinutes = "Discussed delivery plan." });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

        var sent = await WaitForCheckOutMailAsync(visitNumber);
        Assert.Equal("Discussed delivery plan.", sent.MeetingMinutes);

        var edit = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new { meetingMinutes = "Discussed delivery plan (edited)." });
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
        await Task.Delay(1500);
        Assert.Single(_mail.CheckOutsFor(visitNumber));
    }

    [Fact]
    public async Task CheckOut_With_Mom_Sends_Email_Immediately()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Immediate Mail");

        var admin = await LoginAsync("admin");
        (await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new { meetingMinutes = "Signed the contract." }))
            .EnsureSuccessStatusCode();
        await Task.Delay(500);
        Assert.Empty(_mail.CheckOutsFor(visitNumber));

        var superadmin = await LoginAsync("superadmin");
        (await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true }))
            .EnsureSuccessStatusCode();

        var sent = await WaitForCheckOutMailAsync(visitNumber);
        Assert.Equal("Signed the contract.", sent.MeetingMinutes);
    }

    [Fact]
    public async Task Mom_Not_Required_While_Inside_Sends_Email_At_CheckOut()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Waived Inside");

        var reception = await LoginAsync("reception");
        var waive = await reception.PutAsJsonAsync($"/api/visitors/{visitId}/mom-not-required", new { notRequired = true });
        Assert.True(waive.IsSuccessStatusCode, await waive.Content.ReadAsStringAsync());
        var detail = (await waive.Content.ReadFromJsonAsync<ApiResponse<VisitorDetailDto>>(JsonOptions))!.Data!;
        Assert.True(detail.MomNotRequired);
        Assert.Equal("reception", detail.MomNotRequiredBy);

        var superadmin = await LoginAsync("superadmin");
        (await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true }))
            .EnsureSuccessStatusCode();

        var sent = await WaitForCheckOutMailAsync(visitNumber);
        Assert.Equal("Not required", sent.MeetingMinutes);

        var dashboard = (await superadmin.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions))!.Data!;
        var row = Assert.Single(dashboard.CheckedOutWithoutMomItems, v => v.VisitId == visitId);
        Assert.True(row.MomNotRequired);
        Assert.DoesNotContain(dashboard.MomPendingItems, v => v.VisitId == visitId);

        var undo = await superadmin.PutAsJsonAsync($"/api/visitors/{visitId}/mom-not-required", new { notRequired = false });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, undo.StatusCode);
    }

    [Fact]
    public async Task Mom_Not_Required_After_CheckOut_Sends_Held_Email_Once()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Waived Later");

        var superadmin = await LoginAsync("superadmin");
        (await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true }))
            .EnsureSuccessStatusCode();
        await Task.Delay(1000);
        Assert.Empty(_mail.CheckOutsFor(visitNumber));

        var pending = (await superadmin.GetFromJsonAsync<ApiResponse<DashboardDto>>("/api/dashboard", JsonOptions))!.Data!;
        Assert.Contains(pending.CheckedOutWithoutMomItems, v => v.VisitId == visitId && !v.MomNotRequired);

        (await superadmin.PutAsJsonAsync($"/api/visitors/{visitId}/mom-not-required", new { notRequired = true }))
            .EnsureSuccessStatusCode();
        await WaitForCheckOutMailAsync(visitNumber);

        var admin = await LoginAsync("admin");
        (await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new { meetingMinutes = "Added later anyway." }))
            .EnsureSuccessStatusCode();
        await Task.Delay(1000);
        Assert.Single(_mail.CheckOutsFor(visitNumber));
    }

    private async Task<VisitMailContext> WaitForCheckOutMailAsync(string visitNumber)
    {
        for (var i = 0; i < 50; i++)
        {
            var sent = _mail.CheckOutsFor(visitNumber);
            if (sent.Count > 0) return sent[0];
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"No check-out email was sent for visit {visitNumber}.");
    }

    private async Task<(Guid VisitId, string VisitNumber)> RegisterAndCheckInAsync(string namePrefix)
    {
        var admin = await LoginAsync("admin");
        var settings = (await (await admin.GetAsync("/api/settings")).Content
            .ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        settings.ApprovalRequired = false;
        settings.WalkInApprovalRequired = false;
        (await admin.PutAsJsonAsync("/api/settings", settings)).EnsureSuccessStatusCode();

        Guid departmentId, hostId, purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var host = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId).OrderBy(e => e.FullName).FirstAsync();
            var purpose = await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId).OrderBy(p => p.SortOrder).FirstAsync();
            (departmentId, hostId, purposeId) = (host.DepartmentId, host.Id, purpose.Id);
        }

        var name = $"{namePrefix} {Guid.NewGuid():N}"[..40];
        var reception = await LoginAsync("reception");
        var register = await reception.PostAsJsonAsync("/api/visitors", new
        {
            visitorName = name,
            gender = 1,
            companyName = "MoM Mail Co",
            telephone = $"98{Random.Shared.Next(10000000, 99999999)}",
            email = $"{Guid.NewGuid():N}@example.test",
            departmentId,
            hostEmployeeId = hostId,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
            numberOfPersons = 1,
            isWalkIn = true,
            idTypeName = "Aadhaar",
            idNumber = "123456789012",
            photoBase64 = TestPhotos.Base64(name),
            passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
        });
        Assert.True(register.IsSuccessStatusCode, await register.Content.ReadAsStringAsync());
        var data = (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var visitId = data.GetProperty("visitId").GetGuid();
        var visitNumber = data.GetProperty("visitNumber").GetString()!;

        var status = data.GetProperty("statusLabel").GetString();
        if (!string.Equals(status, "Inside", StringComparison.OrdinalIgnoreCase))
        {
            var checkIn = await reception.PostAsJsonAsync($"/api/visitors/{visitId}/check-in", new { });
            Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
        }
        return (visitId, visitNumber);
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

    private sealed class RecordingVisitMailService : IVisitMailService
    {
        private readonly ConcurrentQueue<VisitMailContext> _checkOuts = new();

        public List<VisitMailContext> CheckOutsFor(string visitNumber) =>
            _checkOuts.Where(c => c.VisitNumber == visitNumber).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentAsync(VisitMailContext ctx) => Task.CompletedTask;

        public Task SendCheckOutAsync(VisitMailContext ctx)
        {
            _checkOuts.Enqueue(ctx);
            return Task.CompletedTask;
        }
    }
}
