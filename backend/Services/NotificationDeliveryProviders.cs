using Tiaano.Vms.Api.Models;

namespace Tiaano.Vms.Api.Services;

public interface INotificationDeliveryProvider
{
    string Channel { get; }
    Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default);
}

/// <summary>Email provider that can optionally skip Development mail-pickup fallback (used by SMTP test).</summary>
public interface IEmailNotificationProvider : INotificationDeliveryProvider
{
    /// <summary>
    /// Send via real SMTP only — never write a local .eml pickup file, and never fall back to the company mailbox
    /// when the server refuses the row's From address.
    /// </summary>
    Task DeliverStrictAsync(NotificationOutbox row, CancellationToken ct = default);
}

public sealed class EmailNotificationProvider : IEmailNotificationProvider
{
    private readonly ISettingsService _settings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailNotificationProvider> _logger;
    private readonly bool _sendAsHostOnly;

    public EmailNotificationProvider(
        ISettingsService settings,
        IWebHostEnvironment env,
        ILogger<EmailNotificationProvider> logger,
        IConfiguration configuration)
    {
        _settings = settings;
        _env = env;
        _logger = logger;
        _sendAsHostOnly = configuration.GetValue<bool>("Mail:SendAsHostOnly");
    }

    public string Channel => "Email";

    public Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default) =>
        DeliverCoreAsync(row, allowDevPickupFallback: true, ct);

    public Task DeliverStrictAsync(NotificationOutbox row, CancellationToken ct = default) =>
        DeliverCoreAsync(row, allowDevPickupFallback: false, ct);

    private async Task DeliverCoreAsync(NotificationOutbox row, bool allowDevPickupFallback, CancellationToken ct)
    {
        var smtp = await _settings.ResolveSmtpAsync(row.LocationId, ct);
        if (!smtp.Enabled)
            throw new InvalidOperationException("SMTP disabled");

        try
        {
            if (string.IsNullOrWhiteSpace(smtp.Host))
            {
                if (!allowDevPickupFallback)
                    throw new InvalidOperationException("SMTP host is not configured.");
                await WritePickupAsync(row, smtp, ct);
            }
            else
            {
                await SendViaSmtpAsync(row, smtp, strictSender: _sendAsHostOnly || !allowDevPickupFallback, ct);
            }
        }
        catch (Exception ex) when (allowDevPickupFallback && _env.IsDevelopment() && smtp.FallbackToPickupOnFailure)
        {
            // Local Dev: do not block visitor ops when the company SMTP cert/password is broken.
            _logger.LogWarning(
                ex,
                "SMTP delivery failed in Development for {Recipient}; writing .eml to mail-pickup instead.",
                row.Recipient);
            await WritePickupAsync(row, smtp, ct);
            _logger.LogWarning("Email NOT sent to {Recipient} (saved to mail-pickup): {Subject}", row.Recipient, row.Subject);
            return;
        }

        _logger.LogInformation("Email delivered to {Recipient}: {Subject}", row.Recipient, row.Subject);
    }

    private async Task SendViaSmtpAsync(NotificationOutbox row, SmtpRuntimeOptions smtp, bool strictSender, CancellationToken ct)
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

        try
        {
            await SendMessageAsync(BuildMessage(row, smtp, sendAsOverride: true), smtp, ignoreSsl, ct);
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex) when (
            !string.IsNullOrWhiteSpace(row.FromAddress)
            && ex.ErrorCode is MailKit.Net.Smtp.SmtpErrorCode.SenderNotAccepted or MailKit.Net.Smtp.SmtpErrorCode.MessageNotAccepted)
        {
            if (strictSender)
            {
                throw new InvalidOperationException(
                    $"The mail server refused to send as {row.FromAddress.Trim()}. Give the SMTP login " +
                    $"'{smtp.Username}' Send As permission on that mailbox. Server said: {ex.Message}", ex);
            }

            // The mailbox is not allowed to send as the host (no "Send As" right): still deliver, from the
            // company mailbox with the host's name and Reply-To, rather than losing the mail.
            _logger.LogWarning(ex,
                "SMTP refused sending as {From}; resending to {Recipient} from the configured mailbox.",
                row.FromAddress, row.Recipient);
            await SendMessageAsync(BuildMessage(row, smtp, sendAsOverride: false), smtp, ignoreSsl, ct);
        }
    }

    /// <summary>
    /// Display name for the From header. When the company mailbox sends on someone's behalf the name says so
    /// ("Suresh Baskar via Tiaano"), so it is never mistaken for a message from that person's own address.
    /// </summary>
    private static string SenderDisplayName(NotificationOutbox row, SmtpRuntimeOptions smtp, bool usingOverrideAddress)
    {
        if (string.IsNullOrWhiteSpace(row.FromName)) return smtp.FromName ?? string.Empty;
        if (usingOverrideAddress || string.IsNullOrWhiteSpace(smtp.FromName)) return row.FromName;
        return $"{row.FromName} via {smtp.FromName}";
    }

    private static MimeKit.MimeMessage BuildMessage(NotificationOutbox row, SmtpRuntimeOptions smtp, bool sendAsOverride)
    {
        var useOverride = sendAsOverride && !string.IsNullOrWhiteSpace(row.FromAddress);
        var message = new MimeKit.MimeMessage();
        message.From.Add(new MimeKit.MailboxAddress(
            SenderDisplayName(row, smtp, useOverride),
            useOverride ? row.FromAddress!.Trim() : smtp.FromAddress));
        if (!string.IsNullOrWhiteSpace(row.ReplyTo)
            && MimeKit.MailboxAddress.TryParse(row.ReplyTo.Trim(), out var replyTo))
        {
            if (!string.IsNullOrWhiteSpace(row.FromName)) replyTo.Name = row.FromName;
            message.ReplyTo.Add(replyTo);
        }
        message.To.Add(MimeKit.MailboxAddress.Parse(row.Recipient.Trim()));
        message.Subject = row.Subject;
        message.Body = new MimeKit.TextPart("plain")
        {
            Text = row.Body,
            ContentTransferEncoding = MimeKit.ContentEncoding.QuotedPrintable
        };
        return message;
    }

    private static async Task SendMessageAsync(
        MimeKit.MimeMessage message, SmtpRuntimeOptions smtp, bool ignoreSsl, CancellationToken ct)
    {
        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };
        if (ignoreSsl)
            client.ServerCertificateValidationCallback = static (_, _, _, _) => true;

        // Port 465 is TLS from the first byte; STARTTLS there just hangs until the timeout.
        var socketOptions = smtp.Port == 465
            ? MailKit.Security.SecureSocketOptions.SslOnConnect
            : smtp.EnableSsl
                ? MailKit.Security.SecureSocketOptions.StartTls
                : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;

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
        var fromAddress = !string.IsNullOrWhiteSpace(row.FromAddress)
            ? row.FromAddress
            : string.IsNullOrWhiteSpace(smtp.FromAddress) ? "info@tianode.com" : smtp.FromAddress;
        var fromName = SenderDisplayName(row, smtp, !string.IsNullOrWhiteSpace(row.FromAddress));
        sb.AppendLine(string.IsNullOrWhiteSpace(fromName) ? $"From: {fromAddress}" : $"From: \"{fromName}\" <{fromAddress}>");
        if (!string.IsNullOrWhiteSpace(row.ReplyTo))
            sb.AppendLine($"Reply-To: {row.ReplyTo}");
        sb.AppendLine($"To: {row.Recipient}");
        sb.AppendLine($"Subject: {row.Subject}");
        sb.AppendLine($"Date: {DateTime.UtcNow:R}");
        sb.AppendLine();
        sb.AppendLine(row.Body);
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }
}

/// <summary>Push delivery to the browsers / phones where the recipient (a user id) turned on notifications.</summary>
public sealed class PushNotificationProvider : INotificationDeliveryProvider
{
    private readonly IWebPushService _push;
    private readonly ILogger<PushNotificationProvider> _logger;

    public PushNotificationProvider(IWebPushService push, ILogger<PushNotificationProvider> logger)
    {
        _push = push;
        _logger = logger;
    }

    public string Channel => "Push";

    public async Task DeliverAsync(NotificationOutbox row, CancellationToken ct = default)
    {
        if (row.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Push notification has no tenant.");

        var delivered = await _push.SendToUserAsync(tenantId, row.Recipient,
            new PushMessage(row.Subject, row.Body, row.Link, row.IdempotencyKey), ct);
        if (delivered == 0)
            _logger.LogDebug("No push devices for user {Recipient}: {Subject}", row.Recipient, row.Subject);
    }
}
