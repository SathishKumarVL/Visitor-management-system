using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface INotificationDeliveryProvider
{
    string Channel { get; }
    Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default);
}

public sealed class EmailNotificationProvider : INotificationDeliveryProvider
{
    private readonly ISettingsService _settings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailNotificationProvider> _logger;

    public EmailNotificationProvider(
        ISettingsService settings,
        IWebHostEnvironment env,
        ILogger<EmailNotificationProvider> logger)
    {
        _settings = settings;
        _env = env;
        _logger = logger;
    }

    public string Channel => "Email";

    public async Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default)
    {
        var smtp = await _settings.ResolveSmtpAsync(ct);
        if (!smtp.Enabled)
            throw new InvalidOperationException("SMTP disabled");

        try
        {
            if (string.IsNullOrWhiteSpace(smtp.Host))
                await WritePickupAsync(row, smtp, ct);
            else
                await SendViaSmtpAsync(row, smtp, ct);
        }
        catch (Exception ex) when (_env.IsDevelopment() && smtp.FallbackToPickupOnFailure)
        {
            // Local Dev: do not block visitor ops when the company SMTP cert/password is broken.
            _logger.LogWarning(
                ex,
                "SMTP delivery failed in Development for {Recipient}; writing .eml to mail-pickup instead.",
                row.Recipient);
            await WritePickupAsync(row, smtp, ct);
        }

        _logger.LogInformation("Email delivered to {Recipient}: {Subject}", row.Recipient, row.Subject);
    }

    private async Task SendViaSmtpAsync(NotificationOutbox row, SmtpRuntimeOptions smtp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(smtp.Host))
            throw new InvalidOperationException("Smtp host is required for SMTP delivery.");

        var ignoreSsl = _env.IsDevelopment() && smtp.IgnoreSslErrors;
        if (_env.IsProduction() && smtp.IgnoreSslErrors)
            throw new InvalidOperationException("SMTP TLS certificate validation cannot be disabled in Production.");

        if (string.IsNullOrWhiteSpace(smtp.FromAddress))
            throw new InvalidOperationException("From address is required when SMTP host is configured.");

        _logger.LogInformation(
            "SMTP connect {Host}:{Port} user={User} passwordConfigured={HasPassword}",
            smtp.Host,
            smtp.Port,
            string.IsNullOrEmpty(smtp.Username) ? "(none)" : smtp.Username,
            !string.IsNullOrEmpty(smtp.Password));

        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress(smtp.FromName, smtp.FromAddress));
        message.To.Add(MimeKit.MailboxAddress.Parse(row.Recipient.Trim()));
        message.Subject = row.Subject;
        message.Body = new MimeKit.TextPart("plain")
        {
            Text = row.Body,
            ContentTransferEncoding = MimeKit.ContentEncoding.QuotedPrintable
        };

        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };
        if (ignoreSsl)
            client.ServerCertificateValidationCallback = static (_, _, _, _) => true;

        var socketOptions = smtp.EnableSsl
            ? MailKit.Security.SecureSocketOptions.StartTls
            : MailKit.Security.SecureSocketOptions.None;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(smtp.TimeoutSeconds));
        await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, cts.Token);
        client.AuthenticationMechanisms.Remove("XOAUTH2");

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            if (string.IsNullOrEmpty(smtp.Password))
                throw new InvalidOperationException("SMTP password is required when username is set.");
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cts.Token);
        }

        await client.SendAsync(message, cts.Token);
        await client.DisconnectAsync(true, cts.Token);
    }

    private async Task WritePickupAsync(NotificationOutbox row, SmtpRuntimeOptions smtp, CancellationToken ct)
    {
        var pickup = smtp.PickupDirectory;
        if (string.IsNullOrWhiteSpace(pickup))
            pickup = Path.Combine(_env.ContentRootPath, "App_Data", "mail-pickup");
        Directory.CreateDirectory(pickup);

        var path = Path.Combine(pickup, $"{Guid.NewGuid():N}.eml");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"From: {(string.IsNullOrWhiteSpace(smtp.FromAddress) ? "info@tianode.com" : smtp.FromAddress)}");
        sb.AppendLine($"To: {row.Recipient}");
        sb.AppendLine($"Subject: {row.Subject}");
        sb.AppendLine($"Date: {DateTime.UtcNow:R}");
        sb.AppendLine();
        sb.AppendLine(row.Body);
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }
}

/// <summary>Stub push delivery — logs and marks the outbox row as handled by the caller.</summary>
public sealed class PushNotificationProvider : INotificationDeliveryProvider
{
    private readonly ILogger<PushNotificationProvider> _logger;

    public PushNotificationProvider(ILogger<PushNotificationProvider> logger) => _logger = logger;

    public string Channel => "Push";

    public Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Push stub delivery to user/token {Recipient}: {Subject}",
            row.Recipient,
            row.Subject);
        return Task.CompletedTask;
    }
}
