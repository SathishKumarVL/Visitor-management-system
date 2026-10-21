using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

public class EmployeeNotificationTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public EmployeeNotificationTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Mom_Reminder_Goes_To_The_Host_Once_After_The_Delay()
    {
        // Far in the future, so no other test's check-outs fall inside the reminder window.
        var now = DateTime.UtcNow.AddDays(400 + Random.Shared.Next(5000));
        var tag = Guid.NewGuid().ToString("N")[..6];
        var due = await SeedVisitAsync($"Due {tag}", VisitStatus.CheckedOut, checkOut: now.AddHours(-3));
        var tooSoon = await SeedVisitAsync($"Soon {tag}", VisitStatus.CheckedOut, checkOut: now.AddMinutes(-30));
        var waived = await SeedVisitAsync($"Waived {tag}", VisitStatus.CheckedOut, checkOut: now.AddHours(-3), momNotRequired: true);
        try
        {
            using var scope = _factory.Services.CreateScope();
            var reminders = CreateService(scope);
            await reminders.SendMomRemindersAsync(now);
            await reminders.SendMomRemindersAsync(now.AddMinutes(5));

            var rows = await OutboxAsync(scope, "mom-reminder:");
            var email = Assert.Single(rows, r => r.IdempotencyKey == $"mom-reminder:{due}:email");
            Assert.Equal(TestFixtureSeeder.HostEmail, email.Recipient);
            Assert.Contains($"Due {tag}", email.Subject);
            Assert.Contains($"https://vms.example.test/visitors/{due}/minutes", email.Body);
            var push = Assert.Single(rows, r => r.IdempotencyKey == $"mom-reminder:{due}:push");
            Assert.Equal($"/visitors/{due}/minutes", push.Link);
            Assert.DoesNotContain(rows, r => r.IdempotencyKey!.Contains(tooSoon.ToString()) || r.IdempotencyKey!.Contains(waived.ToString()));
        }
        finally
        {
            await CleanupAsync([due, tooSoon, waived], []);
        }
    }

    [Fact]
    public async Task Mom_Digest_Lists_Every_Pending_Visit_Once_A_Day()
    {
        var day = AppTime.Today.AddDays(400 + Random.Shared.Next(5000));
        var noonUtc = AppTime.StartOfDayUtc(day).AddHours(12);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var first = await SeedVisitAsync($"First {tag}", VisitStatus.CheckedOut, checkOut: noonUtc.AddDays(-2));
        var second = await SeedVisitAsync($"Second {tag}", VisitStatus.CheckedOut, checkOut: noonUtc.AddHours(-5));
        var hostId = await HostEmployeeIdAsync();
        var key = $"mom-digest:{hostId}:{day:yyyyMMdd}";
        try
        {
            using var scope = _factory.Services.CreateScope();
            var reminders = CreateService(scope, digestTime: "11:30");

            Assert.Equal(0, await reminders.SendMomDigestsAsync(noonUtc.AddHours(-3)));
            await reminders.SendMomDigestsAsync(noonUtc);
            await reminders.SendMomDigestsAsync(noonUtc.AddMinutes(30));

            var rows = await OutboxAsync(scope, key);
            var email = Assert.Single(rows, r => r.IdempotencyKey == $"{key}:email");
            Assert.Equal("2 visits are waiting for minutes of meeting", email.Subject);
            Assert.Contains($"First {tag}", email.Body);
            Assert.Contains($"Second {tag}", email.Body);
            Assert.Contains("https://vms.example.test/visitors/mom-pending", email.Body);
            Assert.Single(rows, r => r.IdempotencyKey == $"{key}:push");
        }
        finally
        {
            await CleanupAsync([first, second], [key]);
        }
    }

    [Fact]
    public async Task Bell_Summary_Shows_The_Hosts_Appointments_Minutes_And_Approvals()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var now = DateTime.UtcNow;
        var expected = await SeedVisitAsync($"Expected {tag}", VisitStatus.Expected, expectedToday: true);
        var mom = await SeedVisitAsync($"Mom {tag}", VisitStatus.CheckedOut, checkOut: now.AddSeconds(30));
        var approval = await SeedVisitAsync($"Approval {tag}", VisitStatus.PendingApproval);
        try
        {
            var client = await LoginAsync("host");
            var response = await client.GetAsync("/api/notifications/summary");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var data = JsonDocument.Parse(body).RootElement.GetProperty("data");

            Assert.True(data.GetProperty("appointmentsAreMine").GetBoolean());
            Assert.Contains(Ids(data, "appointmentsToday"), id => id == expected);
            Assert.Contains(Ids(data, "momPending"), id => id == mom);
            Assert.Contains(Ids(data, "approvals"), id => id == approval);
            Assert.Equal(
                data.GetProperty("appointmentsTodayCount").GetInt32()
                + data.GetProperty("momPendingCount").GetInt32()
                + data.GetProperty("approvalsCount").GetInt32(),
                data.GetProperty("total").GetInt32());
            var momItem = data.GetProperty("momPending").EnumerateArray()
                .First(i => i.GetProperty("visitId").GetGuid() == mom);
            Assert.Equal($"/visitors/{mom}/minutes", momItem.GetProperty("link").GetString());
        }
        finally
        {
            await CleanupAsync([expected, mom, approval], []);
        }
    }

    [Fact]
    public async Task Opening_The_Bell_Clears_The_Badge_Until_Something_New_Arrives()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var before = await SeedVisitAsync($"Seen {tag}", VisitStatus.PendingApproval);
        Guid? after = null;
        try
        {
            var client = await LoginAsync("host");
            static bool IsNew(JsonElement data, Guid id) => data.GetProperty("approvals").EnumerateArray()
                .First(i => i.GetProperty("visitId").GetGuid() == id).GetProperty("isNew").GetBoolean();

            var first = await GetDataAsync(client, "/api/notifications/summary");
            Assert.True(first.GetProperty("unseen").GetInt32() >= 1);
            Assert.True(IsNew(first, before));

            (await client.PostAsync("/api/notifications/seen", null)).EnsureSuccessStatusCode();
            var seen = await GetDataAsync(client, "/api/notifications/summary");
            Assert.False(IsNew(seen, before));
            Assert.Contains(Ids(seen, "approvals"), id => id == before);
            Assert.Equal(first.GetProperty("total").GetInt32(), seen.GetProperty("total").GetInt32());

            after = await SeedVisitAsync($"Arrived {tag}", VisitStatus.PendingApproval);
            var later = await GetDataAsync(client, "/api/notifications/summary");
            Assert.True(IsNew(later, after.Value));
            Assert.True(later.GetProperty("unseen").GetInt32() >= 1);
        }
        finally
        {
            await CleanupAsync(after is Guid a ? [before, a] : [before], []);
        }
    }

    [Fact]
    public async Task Push_Subscription_Can_Be_Turned_On_And_Off()
    {
        var client = await LoginAsync("host");
        var status = await GetDataAsync(client, "/api/notifications/push");
        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("publicKey").GetString()));
        var before = status.GetProperty("devices").GetInt32();

        var endpoint = $"https://push.example.test/send/{Guid.NewGuid():N}";
        using var browserKey = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = browserKey.ExportParameters(false).Q;
        var p256dh = WebPushProtocol.Base64UrlEncode([0x04, .. q.X!, .. q.Y!]);
        var auth = WebPushProtocol.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var subscribe = await client.PostAsJsonAsync("/api/notifications/push/subscribe", new { endpoint, p256dh, auth });
        Assert.True(subscribe.IsSuccessStatusCode, await subscribe.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, (await GetDataAsync(client, "/api/notifications/push")).GetProperty("devices").GetInt32());

        var unsubscribe = await client.PostAsJsonAsync("/api/notifications/push/unsubscribe", new { endpoint });
        Assert.True(unsubscribe.IsSuccessStatusCode);
        Assert.Equal(before, (await GetDataAsync(client, "/api/notifications/push")).GetProperty("devices").GetInt32());

        var insecure = await client.PostAsJsonAsync("/api/notifications/push/subscribe",
            new { endpoint = "http://insecure.example.test/x", p256dh, auth });
        Assert.False(insecure.IsSuccessStatusCode);
        var badKey = await client.PostAsJsonAsync("/api/notifications/push/subscribe",
            new { endpoint, p256dh = "BNotAKey", auth });
        Assert.False(badKey.IsSuccessStatusCode);
    }

    private static IEnumerable<Guid> Ids(JsonElement data, string list) =>
        data.GetProperty(list).EnumerateArray().Select(i => i.GetProperty("visitId").GetGuid());

    private static EmployeeNotificationService CreateService(IServiceScope scope, string digestTime = "18:00")
    {
        var config = new ConfigurationBuilder()
            .AddConfiguration(scope.ServiceProvider.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reminders:MomEnabled"] = "true",
                ["Reminders:MomAfterHours"] = "2",
                ["Reminders:MomDigestTime"] = digestTime,
                ["App:PublicBaseUrl"] = "https://vms.example.test"
            })
            .Build();
        return ActivatorUtilities.CreateInstance<EmployeeNotificationService>(scope.ServiceProvider, (IConfiguration)config);
    }

    private static Task<List<NotificationOutbox>> OutboxAsync(IServiceScope scope, string keyPrefix) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().NotificationOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.IdempotencyKey != null && n.IdempotencyKey.StartsWith(keyPrefix))
            .ToListAsync();

    private async Task<Guid> HostEmployeeIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == WellKnownTenants.TiaanoId && e.Email == TestFixtureSeeder.HostEmail)
            .Select(e => e.Id)
            .FirstAsync();
    }

    private async Task<Guid> SeedVisitAsync(
        string visitorName,
        VisitStatus status,
        DateTime? checkOut = null,
        bool momNotRequired = false,
        bool expectedToday = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var host = await db.Employees.IgnoreQueryFilters()
            .FirstAsync(e => e.TenantId == WellKnownTenants.TiaanoId && e.Email == TestFixtureSeeder.HostEmail);

        var visitor = new Visitor
        {
            TenantId = host.TenantId,
            VisitorNumber = $"EN-{Guid.NewGuid():N}"[..20],
            FullName = visitorName,
            CompanyName = "Notify Co",
            Phone = "9876500001",
            CreatedBy = "test"
        };
        db.Visitors.Add(visitor);

        var visit = new VisitorVisit
        {
            TenantId = host.TenantId,
            VisitorId = visitor.Id,
            VisitNumber = $"EN-{Guid.NewGuid():N}"[..20],
            VisitDate = AppTime.Today,
            VisitTime = TimeOnly.FromDateTime(AppTime.Now),
            ExpectedDate = expectedToday ? AppTime.Today : null,
            ExpectedTime = expectedToday ? TimeOnly.MinValue : null,
            DepartmentId = host.DepartmentId,
            HostEmployeeId = host.Id,
            Status = status,
            CheckInAt = checkOut?.AddHours(-1),
            CheckOutAt = checkOut,
            MomNotRequired = momNotRequired,
            NumberOfPersons = 1,
            CreatedBy = "test"
        };
        db.VisitorVisits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }

    private async Task CleanupAsync(Guid[] visitIds, string[] extraKeyPrefixes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var keys = visitIds.Select(id => id.ToString()).ToList();
        var outbox = await db.NotificationOutbox.IgnoreQueryFilters()
            .Where(n => n.IdempotencyKey != null && n.IdempotencyKey.StartsWith("mom-"))
            .ToListAsync();
        db.NotificationOutbox.RemoveRange(outbox.Where(n =>
            keys.Any(k => n.IdempotencyKey!.Contains(k)) || extraKeyPrefixes.Any(p => n.IdempotencyKey!.StartsWith(p))));

        var visits = await db.VisitorVisits.IgnoreQueryFilters().Where(v => visitIds.Contains(v.Id)).ToListAsync();
        var visitorIds = visits.Select(v => v.VisitorId).ToList();
        db.VisitorVisits.RemoveRange(visits);
        db.Visitors.RemoveRange(await db.Visitors.IgnoreQueryFilters().Where(v => visitorIds.Contains(v.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data");
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = TestSecrets.SeedPassword, rememberMe = false });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = json.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
