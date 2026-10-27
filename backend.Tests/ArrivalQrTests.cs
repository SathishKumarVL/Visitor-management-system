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
using Tiaano.Vms.Api.Services.Face;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>The appointment email's QR opens today's booking at reception; the face still decides who gets in.</summary>
[Collection("CoreWorkflow")]
public class ArrivalQrTests : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RecordingVisitMailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ArrivalQrTests(TestApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVisitMailService>();
            services.AddSingleton<IVisitMailService>(_mail);
        }));
    }

    [Fact]
    public async Task Qr_Opens_Todays_Booking_And_Is_Replaced_By_A_Newer_Email_And_Dies_On_Cancel()
    {
        var reception = await LoginAsync("reception");
        var refs = await RefsAsync();
        var booking = Booking(refs, AppTime.Now.AddMinutes(20));
        var visitId = await BookAsync(reception, booking);
        var first = await CodeForAsync(visitId);
        Assert.StartsWith(ArrivalCodes.Prefix, first);

        var found = await FindAsync(reception, first);
        if (booking.Date != AppTime.Today.ToString("yyyy-MM-dd"))
        {
            // Run just before midnight: the slot fell on tomorrow, and a QR only works on its own day.
            Assert.Equal(HttpStatusCode.BadRequest, found.StatusCode);
            Assert.Contains("works only on that day", await found.Content.ReadAsStringAsync());
            return;
        }
        Assert.True(found.IsSuccessStatusCode, await found.Content.ReadAsStringAsync());
        var data = (await found.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(visitId, data.GetProperty("visitId").GetGuid());
        Assert.Equal(refs.HostName, data.GetProperty("hostName").GetString());

        // A handheld scanner may type the code without the prefix.
        Assert.True((await FindAsync(reception, first[ArrivalCodes.Prefix.Length..])).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FindAsync(reception, $"{ArrivalCodes.Prefix}not-a-real-code-at-all")).StatusCode);

        var edited = await reception.PutAsJsonAsync($"/api/visitors/expected/{visitId}", booking.Payload(refs) with { companyName = "Renamed QR Co" });
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        var second = await CodeForAsync(visitId, after: first);
        Assert.Equal(HttpStatusCode.BadRequest, (await FindAsync(reception, first)).StatusCode);
        Assert.True((await FindAsync(reception, second)).IsSuccessStatusCode);

        (await reception.PostAsJsonAsync($"/api/visitors/expected/{visitId}/cancel", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await FindAsync(reception, second)).StatusCode);
    }

    [Fact]
    public async Task Qr_For_Another_Day_Does_Not_Open_The_Booking()
    {
        var reception = await LoginAsync("reception");
        var refs = await RefsAsync();
        var visitId = await BookAsync(reception, Booking(refs, AppTime.Now.AddDays(2)));
        var code = await CodeForAsync(visitId);

        var found = await FindAsync(reception, code);
        Assert.Equal(HttpStatusCode.BadRequest, found.StatusCode);
        Assert.Contains("works only on that day", await found.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Check_In_Against_A_Booking_Needs_The_Booked_Face_Or_An_Id_Confirmation()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var refs = await RefsAsync();
        var booking = Booking(refs, AppTime.Now.AddMinutes(30));
        var visitId = await BookAsync(reception, booking);
        var bookedFace = TestPhotos.Base64($"booked-{Guid.NewGuid():N}");
        await EnrolFaceForBookingAsync(visitId, bookedFace);

        var stranger = TestPhotos.Base64($"stranger-{Guid.NewGuid():N}");
        Assert.Equal(BookingFaceCheck.Mismatch, await FaceCheckAsync(reception, visitId, stranger));
        Assert.Equal(BookingFaceCheck.Match, await FaceCheckAsync(reception, visitId, bookedFace));

        var refused = await reception.PostAsJsonAsync("/api/visitors", ArrivalBody(refs, booking, visitId, stranger, confirmed: false));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("does not match the photo on this booking", await refused.Content.ReadAsStringAsync());
        Assert.Equal(VisitStatus.Expected, await StatusOfAsync(visitId));

        var confirmed = await reception.PostAsJsonAsync("/api/visitors", ArrivalBody(refs, booking, visitId, stranger, confirmed: true));
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = await db.VisitorVisits.IgnoreQueryFilters().SingleAsync(v => v.Id == visitId);
        Assert.NotEqual(VisitStatus.Expected, visit.Status);
        Assert.Contains("Face did not match the booking photo", visit.Notes);
        Assert.Null(visit.ArrivalCodeHash);
    }

    [Fact]
    public async Task Matching_Face_Checks_In_Without_Confirmation_And_Booking_Without_A_Face_Is_Not_Blocked()
    {
        await SetApprovalRequiredAsync(false);
        var reception = await LoginAsync("reception");
        var refs = await RefsAsync();

        var withFace = Booking(refs, AppTime.Now.AddMinutes(40));
        var withFaceId = await BookAsync(reception, withFace);
        var face = TestPhotos.Base64($"same-{Guid.NewGuid():N}");
        await EnrolFaceForBookingAsync(withFaceId, face);
        var arrived = await reception.PostAsJsonAsync("/api/visitors", ArrivalBody(refs, withFace, withFaceId, face, confirmed: false));
        Assert.True(arrived.IsSuccessStatusCode, await arrived.Content.ReadAsStringAsync());

        var noFace = Booking(refs, AppTime.Now.AddMinutes(50));
        var noFaceId = await BookAsync(reception, noFace);
        var anyPhoto = TestPhotos.Base64($"first-time-{Guid.NewGuid():N}");
        Assert.Equal(BookingFaceCheck.NoReference, await FaceCheckAsync(reception, noFaceId, anyPhoto));
        var firstTime = await reception.PostAsJsonAsync("/api/visitors", ArrivalBody(refs, noFace, noFaceId, anyPhoto, confirmed: false));
        Assert.True(firstTime.IsSuccessStatusCode, await firstTime.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Qr_Text_Is_Removed_From_The_Outbox_Once_Delivered()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Set(WellKnownTenants.TiaanoId);
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var key = $"qr-outbox-test:{Guid.NewGuid():N}";

        await notifications.NotifyAsync("Email", "qr.visitor@example.test", "QR test", "Body", key, qrText: ArrivalCodes.New());

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.NotificationOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.IdempotencyKey == key);
        if (row.IsSent) Assert.Null(row.QrText);
        else Assert.NotNull(row.QrText);
    }

    [Fact]
    public void Qr_Image_Is_A_Png()
    {
        var png = QrImage.Png(ArrivalCodes.New());
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Refs(Guid DepartmentId, Guid HostId, string HostName, Guid PurposeId);

    private sealed record BookingBody(string Name, string Phone, string Email, string Date, string Time)
    {
        public Payload Payload(Refs refs) => new(Name, 1, "QR Test Co", Phone, Email, Date, Time, refs.DepartmentId, refs.HostId,
            [refs.PurposeId], []);
    }

    private sealed record Payload(
        string visitorName, int gender, string companyName, string phone, string email, string expectedDate, string expectedTime,
        Guid departmentId, Guid hostEmployeeId, Guid[] purposeIds, Guid[] locationIds);

    private static BookingBody Booking(Refs refs, DateTime slot) => new(
        $"QR Visitor {Guid.NewGuid():N}"[..24],
        $"9{Random.Shared.Next(100_000_000, 999_999_999)}",
        $"{Guid.NewGuid():N}@example.test",
        slot.ToString("yyyy-MM-dd"),
        slot.ToString("HH:mm"));

    private static object ArrivalBody(Refs refs, BookingBody booking, Guid visitId, string photo, bool confirmed) => new
    {
        visitorName = booking.Name,
        gender = 1,
        companyName = "QR Test Co",
        telephone = booking.Phone,
        email = booking.Email,
        departmentId = refs.DepartmentId,
        hostEmployeeId = refs.HostId,
        purposeIds = new[] { refs.PurposeId },
        locationIds = Array.Empty<Guid>(),
        numberOfPersons = 1,
        isWalkIn = false,
        expectedVisitId = visitId,
        faceMismatchConfirmed = confirmed,
        photoBase64 = photo,
        idTypeName = "Aadhaar",
        idNumber = "123456789012",
        passNumber = $"P-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
    };

    private async Task<Guid> BookAsync(HttpClient client, BookingBody booking)
    {
        var refs = await RefsAsync();
        var create = await client.PostAsJsonAsync("/api/visitors/expected", booking.Payload(refs));
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        return (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("visitId").GetGuid();
    }

    private static Task<HttpResponseMessage> FindAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/visitors/expected/arrival-code", new { code });

    private static async Task<string> FaceCheckAsync(HttpClient client, Guid visitId, string photo)
    {
        var response = await client.PostAsJsonAsync($"/api/visitors/expected/{visitId}/face-check", new { photoBase64 = photo });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("result").GetString()!;
    }

    /// <summary>The visitor's code from the latest appointment email for the booking (newer than <paramref name="after"/>).</summary>
    private async Task<string> CodeForAsync(Guid visitId, string? after = null)
    {
        string visitNumber;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            visitNumber = await db.VisitorVisits.IgnoreQueryFilters().Where(v => v.Id == visitId).Select(v => v.VisitNumber).SingleAsync();
        }
        for (var i = 0; i < 50; i++)
        {
            var code = _mail.CodesFor(visitNumber).LastOrDefault(c => c.Length > 0 && c != after);
            if (code is not null) return code;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException("No appointment email with a QR code was sent.");
    }

    private async Task EnrolFaceForBookingAsync(Guid visitId, string photoBase64)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var faces = scope.ServiceProvider.GetRequiredService<IFaceEmbeddingService>();
        var visit = await db.VisitorVisits.IgnoreQueryFilters().SingleAsync(v => v.Id == visitId);
        var vector = faces.Embed(Convert.FromBase64String(photoBase64[(photoBase64.IndexOf(',') + 1)..])).Vector;
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        db.VisitorFaceDescriptors.Add(new VisitorFaceDescriptor
        {
            TenantId = visit.TenantId,
            VisitorId = visit.VisitorId,
            Descriptor = bytes,
            Dimensions = vector.Length,
            Model = faces.ModelId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<VisitStatus> StatusOfAsync(Guid visitId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.VisitorVisits.IgnoreQueryFilters().Where(v => v.Id == visitId).Select(v => v.Status).SingleAsync();
    }

    private async Task SetApprovalRequiredAsync(bool required)
    {
        var admin = await LoginAsync("superadmin");
        var current = await admin.GetAsync("/api/settings");
        current.EnsureSuccessStatusCode();
        var dto = (await current.Content.ReadFromJsonAsync<ApiResponse<SettingsDto>>(JsonOptions))!.Data!;
        dto.ApprovalRequired = required;
        dto.WalkInApprovalRequired = required;
        (await admin.PutAsJsonAsync("/api/settings", dto)).EnsureSuccessStatusCode();
    }

    private Refs? _refs;

    private async Task<Refs> RefsAsync()
    {
        if (_refs is not null) return _refs;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var host = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(e => e.FullName)
            .FirstAsync();
        var purposeId = (await db.VisitPurposes.IgnoreQueryFilters()
            .Where(p => p.IsActive && p.TenantId == WellKnownTenants.TiaanoId)
            .OrderBy(p => p.SortOrder)
            .FirstAsync()).Id;
        return _refs = new Refs(host.DepartmentId, host.Id, host.FullName, purposeId);
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

        public List<string> CodesFor(string visitNumber) =>
            _appointments.Where(c => c.VisitNumber == visitNumber).Select(c => c.ArrivalCode).ToList();

        public Task SendCheckInAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendCheckOutAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendMeetingMinutesAsync(VisitMailContext ctx) => Task.CompletedTask;
        public Task SendAppointmentCancelledAsync(VisitMailContext ctx, bool hostOnly = false) => Task.CompletedTask;
        public Task<bool> SendVisitorInvitationAsync(VisitMailContext ctx, string idempotencyKey) => Task.FromResult(true);

        public Task SendAppointmentAsync(VisitMailContext ctx)
        {
            _appointments.Enqueue(ctx);
            return Task.CompletedTask;
        }
    }
}
