using System.Collections.Concurrent;
using System.Net;
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

/// <summary>The check-out email goes at check-out; minutes of meeting have their own email, sent by the host.</summary>
[Collection("CoreWorkflow")]
public class CheckOutAndMinutesMailTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public CheckOutAndMinutesMailTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVisitMailService>();
            services.AddSingleton<IVisitMailService>(_mail);
        }));
    }

    [Fact]
    public async Task CheckOut_Email_Goes_Immediately_Without_Waiting_For_Minutes()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Split CheckOut");

        var superadmin = await LoginAsync("superadmin");
        var checkOut = await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true });
        Assert.True(checkOut.IsSuccessStatusCode, await checkOut.Content.ReadAsStringAsync());

        await WaitForAsync(() => _mail.CheckOutsFor(visitNumber), "check-out");
        Assert.Empty(_mail.MinutesFor(visitNumber));
    }

    [Fact]
    public async Task Saving_Minutes_Stores_Every_Section_And_Emails_Only_When_Asked()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Split Save");
        var admin = await LoginAsync("superadmin");

        var save = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new
        {
            meetingMinutes = "Reviewed the delivery schedule.",
            decisions = "Ship batch 2 on Monday.",
            actionItems = new[]
            {
                new { task = "Send revised quote", owner = "Suresh", dueDate = "2026-10-20" },
                new { task = "", owner = "", dueDate = (string?)null },
            },
            nextMeetingDate = "2026-10-27",
            sendEmail = false,
        });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        var detail = (await save.Content.ReadFromJsonAsync<ApiResponse<VisitorDetailDto>>(JsonOptions))!.Data!;
        Assert.Equal("Reviewed the delivery schedule.", detail.MeetingMinutes);
        Assert.Equal("Ship batch 2 on Monday.", detail.MomDecisions);
        var item = Assert.Single(detail.MomActionItems);
        Assert.Equal(("Send revised quote", "Suresh", new DateOnly(2026, 10, 20)), (item.Task, item.Owner, item.DueDate));
        Assert.Equal(new DateOnly(2026, 10, 27), detail.MomNextMeetingDate);
        Assert.Equal("superadmin", detail.MomRecordedBy);
        Assert.Null(detail.MomEmailSentAt);
        Assert.True(detail.HasMeetingMinutes);

        await Task.Delay(1000);
        Assert.Empty(_mail.MinutesFor(visitNumber));

        var send = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new
        {
            meetingMinutes = "Reviewed the delivery schedule.",
            decisions = "Ship batch 2 on Monday.",
            actionItems = new[] { new { task = "Send revised quote", owner = "Suresh", dueDate = "2026-10-20" } },
            nextMeetingDate = "2026-10-27",
            sendEmail = true,
        });
        Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
        Assert.NotNull((await send.Content.ReadFromJsonAsync<ApiResponse<VisitorDetailDto>>(JsonOptions))!.Data!.MomEmailSentAt);

        var sent = (await WaitForAsync(() => _mail.MinutesFor(visitNumber), "minutes"))[0];
        Assert.Equal("Reviewed the delivery schedule.", sent.MeetingMinutes);
        Assert.Equal("Ship batch 2 on Monday.", sent.MomDecisions);
        Assert.Equal("1. Send revised quote (Owner: Suresh) (Due: 20 Oct 2026)", sent.MomActionItems);
        Assert.Equal("27 Oct 2026", sent.MomNextMeetingDate);
        Assert.Empty(_mail.CheckOutsFor(visitNumber));
    }

    [Fact]
    public async Task Minutes_Are_Read_Only_Once_The_Email_Is_Sent()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Split Locked");
        var admin = await LoginAsync("superadmin");

        (await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes",
            new { meetingMinutes = "First version.", sendEmail = true })).EnsureSuccessStatusCode();
        await WaitForAsync(() => _mail.MinutesFor(visitNumber), "minutes");

        var edit = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes",
            new { meetingMinutes = "Corrected version.", sendEmail = false });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Contains("read-only", await edit.Content.ReadAsStringAsync());

        var resend = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes",
            new { meetingMinutes = "Corrected version.", sendEmail = true });
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);

        var detail = (await admin.GetFromJsonAsync<ApiResponse<VisitorDetailDto>>($"/api/visitors/{visitId}", JsonOptions))!.Data!;
        Assert.Equal("First version.", detail.MeetingMinutes);
        await Task.Delay(500);
        Assert.Single(_mail.MinutesFor(visitNumber));
    }

    [Fact]
    public async Task Action_Item_Without_A_Task_Is_Rejected()
    {
        var (visitId, _) = await RegisterAndCheckInAsync("MoM Split Invalid");
        var admin = await LoginAsync("superadmin");

        var save = await admin.PutAsJsonAsync($"/api/visitors/{visitId}/meeting-minutes", new
        {
            meetingMinutes = "Discussed.",
            actionItems = new[] { new { task = "", owner = "Suresh", dueDate = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
    }

    [Fact]
    public async Task Mom_Not_Required_Sends_No_Minutes_Email_And_Can_Be_Undone_After_CheckOut()
    {
        var (visitId, visitNumber) = await RegisterAndCheckInAsync("MoM Split Waived");

        var reception = await LoginAsync("reception");
        var waive = await reception.PutAsJsonAsync($"/api/visitors/{visitId}/mom-not-required", new { notRequired = true });
        Assert.True(waive.IsSuccessStatusCode, await waive.Content.ReadAsStringAsync());
        var detail = (await waive.Content.ReadFromJsonAsync<ApiResponse<VisitorDetailDto>>(JsonOptions))!.Data!;
        Assert.True(detail.MomNotRequired);
        Assert.Equal("reception", detail.MomNotRequiredBy);

        var superadmin = await LoginAsync("superadmin");
        (await superadmin.PostAsJsonAsync($"/api/visitors/{visitId}/check-out", new { skipFeedback = true }))
            .EnsureSuccessStatusCode();
        await WaitForAsync(() => _mail.CheckOutsFor(visitNumber), "check-out");

        async Task<List<VisitorListItemDto>> Listed(string quickFilter) =>
            (await superadmin.GetFromJsonAsync<ApiResponse<PagedResult<VisitorListItemDto>>>(
                $"/api/visitors?quickFilter={quickFilter}&pageSize=100", JsonOptions))!.Data!.Items.ToList();
        Assert.True(Assert.Single(await Listed("momnotrequired"), v => v.VisitId == visitId).MomNotRequired);
        Assert.DoesNotContain(await Listed("momnotsent"), v => v.VisitId == visitId);
        var manual = Assert.Single(await Listed("manualcheckouttoday"), v => v.VisitId == visitId);
        Assert.Equal("Super Administrator", manual.CheckedOutBy);

        var undo = await superadmin.PutAsJsonAsync($"/api/visitors/{visitId}/mom-not-required", new { notRequired = false });
        Assert.True(undo.IsSuccessStatusCode, await undo.Content.ReadAsStringAsync());
        Assert.False((await undo.Content.ReadFromJsonAsync<ApiResponse<VisitorDetailDto>>(JsonOptions))!.Data!.MomNotRequired);

        await Task.Delay(500);
        Assert.Empty(_mail.MinutesFor(visitNumber));
        Assert.Single(_mail.CheckOutsFor(visitNumber));
    }

    private static async Task<List<VisitMailContext>> WaitForAsync(Func<List<VisitMailContext>> read, string what)
    {
        for (var i = 0; i < 50; i++)
        {
            var sent = read();
            if (sent.Count > 0) return sent;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"No {what} email was sent.");
    }

    private async Task<(Guid VisitId, string VisitNumber)> RegisterAndCheckInAsync(string namePrefix)
    {
        var admin = await LoginAsync("superadmin");
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
        private readonly ConcurrentQueue<VisitMailContext> _minutes = new();

        public List<VisitMailContext> CheckOutsFor(string visitNumber) =>
            _checkOuts.Where(c => c.VisitNumber == visitNumber).ToList();

        public List<VisitMailContext> MinutesFor(string visitNumber) =>
            _minutes.Where(c => c.VisitNumber == visitNumber).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentAsync(VisitMailContext ctx) => Task.CompletedTask;

        public Task SendCheckOutAsync(VisitMailContext ctx)
        {
            _checkOuts.Enqueue(ctx);
            return Task.CompletedTask;
        }

        public Task SendMeetingMinutesAsync(VisitMailContext ctx)
        {
            _minutes.Enqueue(ctx);
            return Task.CompletedTask;
        }
    }
}
