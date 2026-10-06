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
            string? idempotencyKey = null, Guid? locationId = null, MailSender? sender = null)
        {
            Sent.Add((recipient, sender, subject, body));
            return Task.CompletedTask;
        }

        public Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient, Guid? locationId = null) =>
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
        bool sendAsHost, bool appointment = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = new CapturingNotifications();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:CheckOutSendAsHost"] = sendAsHost.ToString() })
            .Build();
        var service = new VisitMailService(db, notifications, NullLogger<VisitMailService>.Instance, config);
        if (appointment)
            await service.SendAppointmentAsync(Context());
        else
            await service.SendCheckOutAsync(Context());
        return notifications.Sent;
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
