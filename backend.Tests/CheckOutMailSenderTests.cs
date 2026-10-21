using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Services;
using Xunit;

namespace Tiaano.Vms.Api.Tests;

/// <summary>Mails to the visitor come from the host they meet, not the shared mailbox.</summary>
public class CheckOutMailSenderTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public CheckOutMailSenderTests(TestApiFactory factory) => _factory = factory;

    private sealed class CapturingNotifications : INotificationService
    {
        public List<(string Recipient, MailSender? Sender, string Subject, string Body)> Sent { get; } = [];

        public Task NotifyAsync(string channel, string recipient, string subject, string body,
            string? idempotencyKey = null, Guid? locationId = null, MailSender? sender = null, string? link = null)
        {
            Sent.Add((recipient, sender, subject, body));
            return Task.CompletedTask;
        }

        public Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null, MailSender? sender = null) =>
            Task.FromResult((true, ""));

        public Task<(int Attempted, int Sent)> RetryUnsentEmailsAsync(int take = 20) => Task.FromResult((0, 0));
    }

    private static VisitMailContext Context() => new(
        VisitorName: "Asha",
        Salutation: "Ms.",
        VisitorEmail: "asha@example.test",
        Company: "Acme",
        VisitingPerson: "Suresh Baskar",
        VisitingPersonEmail: "suresh.baskar@example.test",
        VisitorNumber: "VMS-V-20261003-0006",
        VisitNumber: $"VMS-2026-{Guid.NewGuid():N}"[..20],
        Location: "Fibtec",
        LocationId: null,
        Department: "Sales",
        Purpose: "Meeting",
        MeetingMinutes: "",
        MomentLocal: DateTime.Now);

    private async Task<List<(string Recipient, MailSender? Sender, string Subject, string Body)>> SendAsync(
        bool sendAsHost, bool appointment = false, VisitMailContext? minutes = null,
        bool hostOnly = false, VisitMailContext? context = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = new CapturingNotifications();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mail:CheckOutSendAsHost"] = sendAsHost.ToString(),
                ["Mail:SendAsHostOnly"] = hostOnly.ToString(),
            })
            .Build();
        var service = new VisitMailService(db, notifications, NullLogger<VisitMailService>.Instance, config);
        if (minutes is not null)
            await service.SendMeetingMinutesAsync(minutes);
        else if (appointment)
            await service.SendAppointmentAsync(context ?? Context());
        else
            await service.SendCheckOutAsync(context ?? Context());
        return notifications.Sent;
    }

    [Fact]
    public async Task Host_Only_Mode_Sends_Visitor_Mails_From_The_Host_Address()
    {
        var sent = await SendAsync(sendAsHost: false, appointment: true, hostOnly: true);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.Equal("suresh.baskar@example.test", visitorCopy.Sender!.FromAddress);
    }

    [Fact]
    public async Task Host_Only_Mode_Skips_Visitor_Mail_When_Host_Has_No_Email()
    {
        var noHostEmail = Context() with { VisitingPersonEmail = null };

        var checkOut = await SendAsync(sendAsHost: true, hostOnly: true, context: noHostEmail);
        var appointment = await SendAsync(sendAsHost: true, appointment: true, hostOnly: true, context: noHostEmail);

        Assert.Empty(checkOut);
        Assert.Empty(appointment);
    }

    [Fact]
    public async Task CheckOut_Mail_Carries_No_Minutes_Of_Meeting()
    {
        var sent = await SendAsync(sendAsHost: false);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.DoesNotContain("Minutes of meeting", visitorCopy.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{", visitorCopy.Body);
    }

    [Fact]
    public async Task Minutes_Mail_Goes_To_Visitor_From_Host_And_To_Host_With_Every_Section()
    {
        var ctx = Context() with
        {
            MeetingMinutes = "Reviewed the delivery schedule.",
            MomDecisions = "Ship batch 2 on Monday.",
            MomActionItems = "1. Send revised quote (Owner: Suresh) (Due: 20 Oct 2026)",
            MomNextMeetingDate = "27 Oct 2026",
            MomSendKey = Guid.NewGuid().ToString("N"),
        };

        var sent = await SendAsync(sendAsHost: false, minutes: ctx);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.Equal("suresh.baskar@example.test", visitorCopy.Sender!.ReplyTo);
        Assert.Contains("Minutes of meeting", visitorCopy.Subject);
        Assert.Contains("Dear Ms. Asha", visitorCopy.Body);
        Assert.Contains("Reviewed the delivery schedule.", visitorCopy.Body);
        Assert.Contains("Ship batch 2 on Monday.", visitorCopy.Body);
        Assert.Contains("1. Send revised quote (Owner: Suresh) (Due: 20 Oct 2026)", visitorCopy.Body);
        Assert.Contains("Next meeting: 27 Oct 2026", visitorCopy.Body);
        Assert.DoesNotContain("{{", visitorCopy.Body);

        var hostCopy = Assert.Single(sent, s => s.Recipient == "suresh.baskar@example.test");
        Assert.Null(hostCopy.Sender);
        Assert.Equal(visitorCopy.Body, hostCopy.Body);
    }

    [Theory]
    [InlineData("Check-out: {{Date}}\n\nMinutes of meeting:\n{{MeetingMinutes}}\n\nRegards,\nTiaano", "Check-out: {{Date}}\n\nRegards,\nTiaano")]
    [InlineData("Check-out: {{Date}}\nMinutes of meeting: {{MeetingMinutes}}\nRegards", "Check-out: {{Date}}\nRegards")]
    [InlineData("Check-out: {{Date}}\r\n\r\nMinutes of meeting:\r\n{{MeetingMinutes}}\r\n", "Check-out: {{Date}}\r\n")]
    [InlineData("Thanks!\n\nRegards", "Thanks!\n\nRegards")]
    public void Saved_CheckOut_Template_Loses_The_Minutes_Section(string body, string expected)
    {
        Assert.Equal(expected, VisitMailService.RemoveMeetingMinutesSection(body));
    }

    [Fact]
    public async Task Appointment_Mail_Goes_To_Host_And_A_Confirmation_To_The_Visitor()
    {
        var sent = await SendAsync(sendAsHost: false, appointment: true);

        var hostCopy = Assert.Single(sent, s => s.Recipient == "suresh.baskar@example.test");
        Assert.Contains("Dear Suresh Baskar", hostCopy.Body);
        Assert.Null(hostCopy.Sender);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.Contains("Dear Ms. Asha", visitorCopy.Body);
        Assert.Contains("Suresh Baskar", visitorCopy.Body);
        Assert.Equal("suresh.baskar@example.test", visitorCopy.Sender!.ReplyTo);

        Assert.Contains("Purpose: Meeting", hostCopy.Body);
        Assert.Contains("Purpose: Meeting", visitorCopy.Body);
    }

    [Theory]
    [InlineData("Dear X,\n\nDepartment: {{Department}}\nDate/Time: {{Date}}\n\nRegards,\nTiaano", "Department: {{Department}}\nPurpose: {{Purpose}}\n")]
    [InlineData("Dear X,\n\nDate/Time: {{Date}}\n\nRegards,\nTiaano", "Purpose: {{Purpose}}\n\nRegards,")]
    [InlineData("Dear X,\r\n\r\n    Department: {{Department}}\r\nRegards", "    Department: {{Department}}\r\n    Purpose: {{Purpose}}\r\n")]
    public void Saved_Appointment_Template_Without_Purpose_Gets_The_Line(string body, string expected)
    {
        var result = VisitMailService.EnsurePurposeLine(body);
        Assert.Contains(expected, result);
        Assert.Equal(result, VisitMailService.EnsurePurposeLine(result));
    }

    [Fact]
    public async Task Visitor_Copy_Shows_Host_Name_And_Replies_Go_To_Host()
    {
        var sent = await SendAsync(sendAsHost: false);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.NotNull(visitorCopy.Sender);
        Assert.Null(visitorCopy.Sender!.FromAddress);
        Assert.Equal("Suresh Baskar", visitorCopy.Sender.FromName);
        Assert.Equal("suresh.baskar@example.test", visitorCopy.Sender.ReplyTo);

        var hostCopy = Assert.Single(sent, s => s.Recipient == "suresh.baskar@example.test");
        Assert.Null(hostCopy.Sender);
    }

    [Fact]
    public async Task Send_As_Host_Uses_Host_Address_As_Sender()
    {
        var sent = await SendAsync(sendAsHost: true);

        var visitorCopy = Assert.Single(sent, s => s.Recipient == "asha@example.test");
        Assert.Equal("suresh.baskar@example.test", visitorCopy.Sender!.FromAddress);
    }
}
