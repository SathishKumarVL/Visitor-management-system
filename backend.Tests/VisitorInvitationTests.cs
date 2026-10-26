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
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>An employee sends a single-use link; the visitor fills in their details and face, which books the visit.</summary>
[Collection("CoreWorkflow")]
public class VisitorInvitationTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public VisitorInvitationTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("App:PublicBaseUrl", "https://vms.example.test");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVisitMailService>();
                services.AddSingleton<IVisitMailService>(_mail);
            });
        });
    }

    [Fact]
    public async Task Visitor_Books_Once_From_The_Link_With_Their_Face()
    {
        var reception = await LoginAsync("reception");
        var host = await AnyHostAsync();
        var email = $"{Guid.NewGuid():N}@example.test";
        var invitationId = await InviteAsync(reception, email, host.Id);
        var token = LatestToken(email);
        var anonymous = _factory.CreateClient();

        var open = await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token });
        var openBody = await open.Content.ReadAsStringAsync();
        Assert.True(open.IsSuccessStatusCode, openBody);
        var opened = JsonDocument.Parse(openBody).RootElement.GetProperty("data");
        Assert.Equal(email, opened.GetProperty("email").GetString());
        Assert.Equal(host.Name, opened.GetProperty("hostName").GetString());
        var purposeId = opened.GetProperty("purposes").EnumerateArray().First().GetProperty("id").GetGuid();

        var phone = RandomPhone();
        var noFace = await anonymous.PostAsJsonAsync("/api/public/invitations/submit",
            SubmitBody(token, phone, purposeId, TestPhotos.NoFaceBase64()));
        Assert.Equal(HttpStatusCode.BadRequest, noFace.StatusCode);

        // Someone else's mobile number must not hand this visitor that person's record and face.
        var otherVisitorPhone = await ExistingVisitorPhoneWithOtherEmailAsync(reception, host);
        var hijack = await anonymous.PostAsJsonAsync("/api/public/invitations/submit",
            SubmitBody(token, otherVisitorPhone, purposeId, TestPhotos.Base64($"hijack-{Guid.NewGuid():N}")));
        Assert.Equal(HttpStatusCode.BadRequest, hijack.StatusCode);
        Assert.Contains("already registered", await hijack.Content.ReadAsStringAsync());

        var submit = await anonymous.PostAsJsonAsync("/api/public/invitations/submit",
            SubmitBody(token, phone, purposeId, TestPhotos.Base64($"invite-{Guid.NewGuid():N}")));
        var submitBody = await submit.Content.ReadAsStringAsync();
        Assert.True(submit.IsSuccessStatusCode, submitBody);
        var visitNumber = JsonDocument.Parse(submitBody).RootElement.GetProperty("data").GetProperty("visitNumber").GetString()!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visit = await db.VisitorVisits.IgnoreQueryFilters().Include(v => v.Visitor).ThenInclude(v => v.Photos)
                .SingleAsync(v => v.VisitNumber == visitNumber);
            Assert.Equal(VisitStatus.Expected, visit.Status);
            Assert.Equal(host.Id, visit.HostEmployeeId);
            Assert.Equal(email, visit.Visitor.Email);
            Assert.NotEmpty(visit.Visitor.Photos);

            var invitation = await db.VisitorInvitations.IgnoreQueryFilters().SingleAsync(i => i.Id == invitationId);
            Assert.Equal(VisitorInvitationStatus.Completed, invitation.Status);
            Assert.Equal(visit.Id, invitation.VisitorVisitId);
        }
        await WaitForAsync(() => _mail.AppointmentsFor(visitNumber), "appointment");

        var again = await anonymous.PostAsJsonAsync("/api/public/invitations/submit",
            SubmitBody(token, RandomPhone(), purposeId, TestPhotos.Base64($"again-{Guid.NewGuid():N}")));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var reopen = await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
        Assert.Contains("already been used", await reopen.Content.ReadAsStringAsync());

        var list = await reception.GetFromJsonAsync<ApiResponse<List<VisitorInvitationDto>>>("/api/visitors/invitations", JsonOptions);
        var listed = Assert.Single(list!.Data!, i => i.Id == invitationId);
        Assert.Equal("Completed", listed.Status);
        Assert.False(string.IsNullOrEmpty(listed.VisitorName));
    }

    [Fact]
    public async Task Replaced_Withdrawn_And_Expired_Links_Stop_Working()
    {
        var reception = await LoginAsync("reception");
        var host = await AnyHostAsync();
        var anonymous = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";

        var id = await InviteAsync(reception, email, host.Id);
        var first = LatestToken(email);
        (await reception.PostAsync($"/api/visitors/invitations/{id}/resend", null)).EnsureSuccessStatusCode();
        var second = LatestToken(email);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token = first })).StatusCode);
        (await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token = second })).EnsureSuccessStatusCode();

        // Inviting the same person to the same employee again reuses the open invitation.
        Assert.Equal(id, await InviteAsync(reception, email, host.Id));
        var third = LatestToken(email);

        (await reception.PostAsync($"/api/visitors/invitations/{id}/withdraw", null)).EnsureSuccessStatusCode();
        var withdrawn = await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token = third });
        Assert.Equal(HttpStatusCode.BadRequest, withdrawn.StatusCode);
        Assert.Contains("withdrawn", await withdrawn.Content.ReadAsStringAsync());

        var expiringEmail = $"{Guid.NewGuid():N}@example.test";
        var expiringId = await InviteAsync(reception, expiringEmail, host.Id);
        var expiringToken = LatestToken(expiringEmail);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.VisitorInvitations.IgnoreQueryFilters().Where(i => i.Id == expiringId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }
        var expired = await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token = expiringToken });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains("expired", await expired.Content.ReadAsStringAsync());

        var garbage = await anonymous.PostAsJsonAsync("/api/public/invitations/open", new { token = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
    }

    [Fact]
    public async Task A_Host_Invites_Only_For_Themselves()
    {
        var hostClient = await LoginAsync("host");
        Guid ownEmployeeId;
        Guid otherEmployeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hostUserId = await db.Users.IgnoreQueryFilters().Where(u => u.UserName == "host").Select(u => u.Id).SingleAsync();
            ownEmployeeId = await db.Employees.IgnoreQueryFilters().Where(e => e.UserId == hostUserId && e.IsActive).Select(e => e.Id).FirstAsync();
            otherEmployeeId = await db.Employees.IgnoreQueryFilters()
                .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId && e.Id != ownEmployeeId)
                .Select(e => e.Id).FirstAsync();
        }

        var forOther = await hostClient.PostAsJsonAsync("/api/visitors/invitations",
            new { email = $"{Guid.NewGuid():N}@example.test", hostEmployeeId = otherEmployeeId });
        Assert.Equal(HttpStatusCode.Forbidden, forOther.StatusCode);

        var email = $"{Guid.NewGuid():N}@example.test";
        var own = await hostClient.PostAsJsonAsync("/api/visitors/invitations", new { email });
        var body = await own.Content.ReadAsStringAsync();
        Assert.True(own.IsSuccessStatusCode, body);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal(ownEmployeeId, data.GetProperty("hostEmployeeId").GetGuid());

        var list = await hostClient.GetFromJsonAsync<ApiResponse<List<VisitorInvitationDto>>>("/api/visitors/invitations", JsonOptions);
        Assert.All(list!.Data!, i => Assert.Equal(ownEmployeeId, i.HostEmployeeId));
        Assert.Contains(list.Data!, i => i.Email == email);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Host(Guid Id, Guid DepartmentId, string Name);

    private static string RandomPhone() => $"9{Random.Shared.Next(100_000_000, 999_999_999)}";

    private static object SubmitBody(string token, string phone, Guid purposeId, string photo)
    {
        var slot = AppTime.Now.AddHours(3);
        return new
        {
            token,
            visitorName = $"Invited {Guid.NewGuid():N}"[..24],
            companyName = "Invited Co",
            gender = 2,
            phone,
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            purposeIds = new[] { purposeId },
            photoBase64 = photo,
        };
    }

    private static async Task<Guid> InviteAsync(HttpClient client, string email, Guid hostId)
    {
        var response = await client.PostAsJsonAsync("/api/visitors/invitations", new { email, hostEmployeeId = hostId });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private string LatestToken(string email)
    {
        var link = _mail.InvitationsTo(email).Last().InviteLink;
        Assert.StartsWith("https://vms.example.test/invite#", link);
        return link[(link.IndexOf('#') + 1)..];
    }

    private async Task<string> ExistingVisitorPhoneWithOtherEmailAsync(HttpClient reception, Host host)
    {
        var phone = RandomPhone();
        var slot = AppTime.Now.AddHours(5);
        Guid purposeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            purposeId = (await db.VisitPurposes.IgnoreQueryFilters()
                .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
                .OrderBy(p => p.SortOrder).FirstAsync()).Id;
        }
        var create = await reception.PostAsJsonAsync("/api/visitors/expected", new
        {
            visitorName = "Existing Visitor",
            gender = 1,
            companyName = "Existing Co",
            phone,
            email = $"{Guid.NewGuid():N}@example.test",
            expectedDate = slot.ToString("yyyy-MM-dd"),
            expectedTime = slot.ToString("HH:mm"),
            departmentId = host.DepartmentId,
            hostEmployeeId = host.Id,
            purposeIds = new[] { purposeId },
            locationIds = Array.Empty<Guid>(),
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        return phone;
    }

    private async Task<Host> AnyHostAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(e => e.FullName)
            .Select(e => new Host(e.Id, e.DepartmentId, e.FullName))
            .FirstAsync();
    }

    private static async Task<List<T>> WaitForAsync<T>(Func<List<T>> read, string what)
    {
        for (var i = 0; i < 50; i++)
        {
            var sent = read();
            if (sent.Count > 0) return sent;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"No {what} email was sent.");
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
        private readonly ConcurrentQueue<VisitMailContext> _appointments = new();
        private readonly ConcurrentQueue<VisitMailContext> _invitations = new();

        public List<VisitMailContext> AppointmentsFor(string visitNumber) =>
            _appointments.Where(c => c.VisitNumber == visitNumber).ToList();

        public List<VisitMailContext> InvitationsTo(string email) =>
            _invitations.Where(c => c.VisitorEmail == email).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendCheckOutAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendMeetingMinutesAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentCancelledAsync(VisitMailContext ctx, bool hostOnly = false) => Task.CompletedTask;

        public Task SendAppointmentAsync(VisitMailContext ctx)
        {
            _appointments.Enqueue(ctx);
            return Task.CompletedTask;
        }

        public Task<bool> SendVisitorInvitationAsync(VisitMailContext ctx, string idempotencyKey)
        {
            _invitations.Enqueue(ctx);
            return Task.FromResult(true);
        }
    }
}
