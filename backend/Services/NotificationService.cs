using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface INotificationService
{
    Task NotifyAsync(string channel, string recipient, string subject, string body);
    Task SendVisitorThankYouEmailAsync(string visitorName, string? email, DateTime visitDateTimeLocal);
    Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext db,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task NotifyAsync(string channel, string recipient, string subject, string body)
    {
        var row = new NotificationOutbox
        {
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        await _db.SaveChangesAsync();

        if (string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
            await TryDeliverAsync(row);
        else
            _logger.LogInformation("Notification queued via {Channel} to {Recipient}: {Subject}", channel, recipient, subject);
    }

    public async Task SendVisitorThankYouEmailAsync(string visitorName, string? email, DateTime visitDateTimeLocal)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Checkout thank-you email skipped — visitor {Name} has no email.", visitorName);
            return;
        }

        var when = visitDateTimeLocal.ToString("MMM dd, yyyy hh:mm tt");
        var subject = "Thank you for visiting Tiaano";
        var body = BuildThankYouBody(visitorName.Trim(), when);

        var row = new NotificationOutbox
        {
            Channel = "Email",
            Recipient = email.Trim(),
            Subject = subject,
            Body = body,
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        await _db.SaveChangesAsync();
        await TryDeliverAsync(row);
    }

    public async Task<(bool Ok, string Message)> SendTestEmailAsync(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return (false, "Recipient email is required.");

        var row = new NotificationOutbox
        {
            Channel = "Email",
            Recipient = recipient.Trim(),
            Subject = "Tiaano VMS SMTP test",
            Body = $"SMTP test from Tiaano Visitor Management at {DateTime.Now:dd MMM yyyy HH:mm:ss}.",
            IsSent = false
        };
        _db.NotificationOutbox.Add(row);
        await _db.SaveChangesAsync();
        await TryDeliverAsync(row);
        return row.IsSent
            ? (true, $"Email sent to {row.Recipient}.")
            : (false, row.Error ?? "Send failed.");
    }

    private static string BuildThankYouBody(string visitorName, string visitWhen) =>
        $"""
        Dear {visitorName},

        Greetings from Tiaano. Thank you so much for taking the time to visit ({visitWhen}) our company. Your support and enthusiasm are invaluable.

        Your ongoing support has been instrumental in our success as a company. We look forward to continuing our business relationship all in the future.

        With Warm Regards,
        ABILASH.C
        SENIOR MANAGER - FACILITATION

        TI ANODE FABRICATORS PVT. LTD.
        Tiaano Bhavan, No. 48, Noothanchary, Madambakkam, Chennai-126, INDIA
        Phone: +91 9444569900 Mobile: +91 8939663774
        Email: sales@tiaano.com Website: www.tianode.com
        """;

    private async Task TryDeliverAsync(NotificationOutbox row)
    {
        var enabled = _config.GetValue("Smtp:Enabled", true);
        if (!enabled)
        {
            row.Error = "SMTP disabled";
            await _db.SaveChangesAsync();
            return;
        }

        try
        {
            var host = (_config["Smtp:Host"] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(host))
                await WritePickupAsync(row);
            else
                await SendViaSmtpAsync(row);

            row.IsSent = true;
            row.SentAt = DateTime.UtcNow;
            row.Error = null;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Email sent to {Recipient}: {Subject}", row.Recipient, row.Subject);
        }
        catch (Exception ex)
        {
            row.IsSent = false;
            var detail = ex.GetBaseException().Message;
            row.Error = detail.Length > 480 ? detail[..480] : detail;
            await _db.SaveChangesAsync();
            _logger.LogError(ex, "Failed to send email to {Recipient}", row.Recipient);
        }
    }

    private async Task SendViaSmtpAsync(NotificationOutbox row)
    {
        var host = (_config["Smtp:Host"] ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Smtp:Host is required for SMTP delivery.");
        var port = _config.GetValue("Smtp:Port", 587);
        var timeoutSeconds = Math.Clamp(_config.GetValue("Smtp:TimeoutSeconds", 45), 10, 120);
        var ignoreSsl = _env.IsDevelopment() && _config.GetValue("Smtp:IgnoreSslErrors", false);
        if (_env.IsProduction() && _config.GetValue("Smtp:IgnoreSslErrors", false))
            throw new InvalidOperationException("SMTP TLS certificate validation cannot be disabled in Production.");

        var user = (_config["Smtp:Username"] ?? "").Trim();
        var pass = _config["Smtp:Password"] ?? "";
        var fromAddress = (_config["Smtp:FromAddress"] ?? "").Trim();
        var fromName = (_config["Smtp:FromName"] ?? "Visitor Management").Trim();

        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException("Smtp:FromAddress is required when SMTP host is configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(row.Recipient.Trim()));
        message.Subject = row.Subject;
        message.Body = new TextPart("plain")
        {
            Text = row.Body,
            ContentTransferEncoding = ContentEncoding.QuotedPrintable
        };

        using var client = new SmtpClient { Timeout = timeoutSeconds * 1000 };
        if (ignoreSsl)
            client.ServerCertificateValidationCallback = static (_, _, _, _) => true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cts.Token);
        client.AuthenticationMechanisms.Remove("XOAUTH2");

        if (!string.IsNullOrEmpty(user))
        {
            if (string.IsNullOrEmpty(pass))
                throw new InvalidOperationException("Smtp:Password is required when Smtp:Username is set.");
            await client.AuthenticateAsync(user, pass, cts.Token);
        }

        await client.SendAsync(message, cts.Token);
        await client.DisconnectAsync(true, cts.Token);

        _logger.LogInformation("SMTP success via {Host}:{Port}", host, port);
    }

    private async Task WritePickupAsync(NotificationOutbox row)
    {
        var pickup = _config["Smtp:PickupDirectory"];
        if (string.IsNullOrWhiteSpace(pickup))
            pickup = Path.Combine(_env.ContentRootPath, "App_Data", "mail-pickup");
        Directory.CreateDirectory(pickup);

        var path = Path.Combine(pickup, $"{Guid.NewGuid():N}.eml");
        var sb = new StringBuilder();
        sb.AppendLine($"From: {_config["Smtp:FromAddress"] ?? "info@tianode.com"}");
        sb.AppendLine($"To: {row.Recipient}");
        sb.AppendLine($"Subject: {row.Subject}");
        sb.AppendLine($"Date: {DateTime.UtcNow:R}");
        sb.AppendLine();
        sb.AppendLine(row.Body);
        await File.WriteAllTextAsync(path, sb.ToString());
    }
}
