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

/// <summary>Cancelling or changing a booking tells the people who expect the visit.</summary>
[Collection("CoreWorkflow")]
public class AppointmentChangeMailTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AppointmentChangeMailTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVisitMailService>();
            services.AddSingleton<IVisitMailService>(_mail);
        }));
    }

    [Fact]
    public async Task Cancelled_Booking_Leaves_The_List_Emails_Both_Sides_And_Cannot_Be_Used()
    {
        var reception = await LoginAsync("reception");
        var (hostA, _) = await TwoHostsAsync();
        var booking = BookingBody(hostA, "Cancel Me");
        var (visitId, visitNumber) = await BookAsync(reception, booking);

        var cancel = await reception.PostAsJsonAsync($"/api/visitors/expected/{visitId}/cancel", new { reason = "Visitor is unwell" });
        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());

        var sent = Assert.Single(await WaitForAsync(() => _mail.CancellationsFor(visitNumber), "cancellation"));
        Assert.Equal("Visitor is unwell", sent.Ctx.CancelReason);
        Assert.False(sent.HostOnly);
        Assert.Equal(booking.Email, sent.Ctx.VisitorEmail);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visit = await db.VisitorVisits.IgnoreQueryFilters().SingleAsync(v => v.Id == visitId);
            Assert.Equal(VisitStatus.Cancelled, visit.Status);
            Assert.Contains("Visitor is unwell", visit.Notes);
        }

        var list = await reception.GetFromJsonAsync<ApiResponse<List<VisitorListItemDto>>>(
            $"/api/visitors/expected?date={booking.ExpectedDate}", JsonOptions);
        Assert.DoesNotContain(list!.Data!, v => v.VisitId == visitId);

        var again = await reception.PostAsJsonAsync($"/api/visitors/expected/{visitId}/cancel", new { });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var edit = await reception.PutAsJsonAsync($"/api/visitors/expected/{visitId}", booking.Payload());
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Fact]
    public async Task Any_Edit_Resends_The_Appointment_Mail_But_An_Unchanged_Save_Does_Not()
    {
        var reception = await LoginAsync("reception");
        var (hostA, _) = await TwoHostsAsync();
        var booking = BookingBody(hostA, "Resend Me");
        var (visitId, visitNumber) = await BookAsync(reception, booking);
        await WaitForAsync(() => _mail.AppointmentsFor(visitNumber), "appointment");

        var renamed = booking with { Company = "Renamed Company Ltd" };
        (await reception.PutAsJsonAsync($"/api/visitors/expected/{visitId}", renamed.Payload())).EnsureSuccessStatusCode();
        var resent = await WaitForAsync(() => _mail.AppointmentsFor(visitNumber).Where(c => c.AppointmentSendKey.Length > 0).ToList(), "updated appointment");
        Assert.Equal("Renamed Company Ltd", Assert.Single(resent).Company);

        (await reception.PutAsJsonAsync($"/api/visitors/expected/{visitId}", renamed.Payload())).EnsureSuccessStatusCode();
        await Task.Delay(1000);
        Assert.Equal(2, _mail.AppointmentsFor(visitNumber).Count);
        Assert.Empty(_mail.CancellationsFor(visitNumber));
    }

    [Fact]
    public async Task Changing_The_Host_Tells_The_Previous_Host_Only()
    {
        var reception = await LoginAsync("reception");
        var (hostA, hostB) = await TwoHostsAsync();
        var booking = BookingBody(hostA, "Move Me");
        var (visitId, visitNumber) = await BookAsync(reception, booking);

        var moved = booking with { Host = hostB };
        var update = await reception.PutAsJsonAsync($"/api/visitors/expected/{visitId}", moved.Payload());
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());

        var cancellation = Assert.Single(await WaitForAsync(() => _mail.CancellationsFor(visitNumber), "previous host"));
        Assert.True(cancellation.HostOnly);
        Assert.Equal(hostA.Name, cancellation.Ctx.VisitingPerson);
        Assert.Null(cancellation.Ctx.VisitorEmail);
        Assert.Contains(hostB.Name, cancellation.Ctx.CancelReason);

        var resent = await WaitForAsync(() => _mail.AppointmentsFor(visitNumber).Where(c => c.AppointmentSendKey.Length > 0).ToList(), "new host");
        Assert.Equal(hostB.Name, Assert.Single(resent).VisitingPerson);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Host(Guid Id, Guid DepartmentId, string Name);

    private sealed record Booking(string Name, string Company, string Phone, string Email, string ExpectedDate, string ExpectedTime, Guid PurposeId, Host Host)
    {
        public object Payload() => new
        {
            visitorName = Name,
            gender = 1,
            companyName = Company,
            phone = Phone,
            email = Email,
            expectedDate = ExpectedDate,
            expectedTime = ExpectedTime,
            departmentId = Host.DepartmentId,
            hostEmployeeId = Host.Id,
            purposeIds = new[] { PurposeId },
            locationIds = Array.Empty<Guid>()
        };
    }

    private Guid _purposeId;

    private Booking BookingBody(Host host, string namePrefix)
    {
        var slot = AppTime.Now.AddHours(3);
        return new Booking(
            $"{namePrefix} {Guid.NewGuid():N}"[..30],
            "Appointment Mail Co",
            $"9{Random.Shared.Next(100_000_000, 999_999_999)}",
            $"{Guid.NewGuid():N}@example.test",
            slot.ToString("yyyy-MM-dd"),
            slot.ToString("HH:mm"),
            _purposeId,
            host);
    }

    private async Task<(Host A, Host B)> TwoHostsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hosts = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(e => e.FullName)
            .Take(2)
            .Select(e => new Host(e.Id, e.DepartmentId, e.FullName))
            .ToListAsync();
        Assert.Equal(2, hosts.Count);
        _purposeId = (await db.VisitPurposes.IgnoreQueryFilters()
            .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(p => p.SortOrder)
            .FirstAsync()).Id;
        return (hosts[0], hosts[1]);
    }

    private static async Task<(Guid VisitId, string VisitNumber)> BookAsync(HttpClient client, Booking booking)
    {
        var create = await client.PostAsJsonAsync("/api/visitors/expected", booking.Payload());
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var data = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        return (data.GetProperty("visitId").GetGuid(), data.GetProperty("visitNumber").GetString()!);
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
        private readonly ConcurrentQueue<(VisitMailContext Ctx, bool HostOnly)> _cancellations = new();

        public List<VisitMailContext> AppointmentsFor(string visitNumber) =>
            _appointments.Where(c => c.VisitNumber == visitNumber).ToList();

        public List<(VisitMailContext Ctx, bool HostOnly)> CancellationsFor(string visitNumber) =>
            _cancellations.Where(c => c.Ctx.VisitNumber == visitNumber).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendCheckOutAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendMeetingMinutesAsync(VisitMailContext ctx) => Task.CompletedTask;

        public Task SendAppointmentAsync(VisitMailContext ctx)
        {
            _appointments.Enqueue(ctx);
            return Task.CompletedTask;
        }

        public Task SendAppointmentCancelledAsync(VisitMailContext ctx, bool hostOnly = false)
        {
            _cancellations.Enqueue((ctx, hostOnly));
            return Task.CompletedTask;
        }
    }
}
