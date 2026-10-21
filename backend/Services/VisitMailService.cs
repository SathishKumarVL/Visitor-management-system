using System.Text;
using Microsoft.EntityFrameworkCore;
using Tiaano.Vms.Api.Data;
using Tiaano.Vms.Api.Models;
using Tiaano.Vms.Api.Models.Enums;

namespace Tiaano.Vms.Api.Services;

public record VisitMailContext(
    string VisitorName,
    string Salutation,
    string? VisitorEmail,
    string Company,
    string VisitingPerson,
    string? VisitingPersonEmail,
    string VisitorNumber,
    string VisitNumber,
    string Location,
    Guid? LocationId,
    string Department,
    string Purpose,
    string MeetingMinutes,
    DateTime MomentLocal,
    string MeetingHall = "",
    string MomDecisions = "",
    string MomActionItems = "",
    string MomNextMeetingDate = "",
    string MomSendKey = "",
    string AppointmentSendKey = "");

/// <summary>Renders Mail Content templates and queues check-in / check-out / appointment / minutes emails.</summary>
public interface IVisitMailService
{
    Task SendCheckInAsync(VisitMailContext ctx);
    Task SendCheckOutAsync(VisitMailContext ctx);
    Task SendAppointmentAsync(VisitMailContext ctx);
    Task SendMeetingMinutesAsync(VisitMailContext ctx);
}

public class VisitMailService : IVisitMailService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<VisitMailService> _logger;
    private readonly bool _checkOutSendAsHost;
    private readonly bool _sendAsHostOnly;

    public VisitMailService(
        ApplicationDbContext db,
        INotificationService notifications,
        ILogger<VisitMailService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
        _sendAsHostOnly = configuration.GetValue<bool>("Mail:SendAsHostOnly");
        _checkOutSendAsHost = _sendAsHostOnly || configuration.GetValue<bool>("Mail:CheckOutSendAsHost");
    }

    /// <summary>
    /// With Mail:SendAsHostOnly a visitor mail must come from the host's own address, so it is not sent at all
    /// when the host has no email.
    /// </summary>
    private bool MaySendVisitorCopy(VisitMailContext ctx, string what)
    {
        if (!_sendAsHostOnly || !string.IsNullOrWhiteSpace(ctx.VisitingPersonEmail)) return true;
        _logger.LogWarning("{What} visitor email skipped — host {Host} has no email to send from.", what, ctx.VisitingPerson);
        return false;
    }

    /// <summary>
    /// Mails to the visitor (appointment confirmation, check-out) come from the person they meet: their name in
    /// From and their address in Reply-To. With Mail:CheckOutSendAsHost the host's address is the actual sender,
    /// which requires the SMTP mailbox to have "Send As" rights for that host and the host's domain to authorise
    /// the SMTP server (SPF/DMARC); otherwise visitors' mail providers reject or spam-file the message.
    /// </summary>
    private MailSender? HostSender(VisitMailContext ctx)
    {
        var hostEmail = ctx.VisitingPersonEmail?.Trim();
        if (string.IsNullOrWhiteSpace(hostEmail)) return null;
        var hostName = string.IsNullOrWhiteSpace(ctx.VisitingPerson) ? null : ctx.VisitingPerson.Trim();
        return new MailSender(_checkOutSendAsHost ? hostEmail : null, hostName, hostEmail);
    }

    public async Task SendCheckInAsync(VisitMailContext ctx)
    {
        // Check-in notifies the visiting person (employee) only.
        if (string.IsNullOrWhiteSpace(ctx.VisitingPersonEmail))
        {
            _logger.LogWarning("Check-in email skipped — host {Host} has no email.", ctx.VisitingPerson);
            return;
        }
        var (subject, body) = await RenderAsync(MailContentKind.CheckIn, ctx);
        await _notifications.NotifyAsync("Email", ctx.VisitingPersonEmail.Trim(), subject, body,
            $"checkin:{ctx.VisitNumber}:{ctx.VisitingPersonEmail.Trim()}", ctx.LocationId);
    }

    public async Task SendCheckOutAsync(VisitMailContext ctx)
    {
        var (subject, body) = await RenderAsync(MailContentKind.CheckOut, ctx);

        if (!string.IsNullOrWhiteSpace(ctx.VisitorEmail))
        {
            if (MaySendVisitorCopy(ctx, "Check-out"))
                await _notifications.NotifyAsync("Email", ctx.VisitorEmail.Trim(), subject, body,
                    $"checkout-visitor:{ctx.VisitNumber}:{ctx.VisitorEmail.Trim()}", ctx.LocationId, HostSender(ctx));
        }
        else
        {
            _logger.LogWarning("Check-out visitor email skipped — {Name} has no email.", ctx.VisitorName);
        }

        if (!string.IsNullOrWhiteSpace(ctx.VisitingPersonEmail))
        {
            await _notifications.NotifyAsync("Email", ctx.VisitingPersonEmail.Trim(), subject, body,
                $"checkout-host:{ctx.VisitNumber}:{ctx.VisitingPersonEmail.Trim()}", ctx.LocationId);
        }
        else
        {
            _logger.LogWarning("Check-out host email skipped — {Host} has no email.", ctx.VisitingPerson);
        }
    }

    /// <summary>
    /// Minutes of meeting go to the visitor (from the host, like check-out) and to the host. Each send is a new
    /// email, so <see cref="VisitMailContext.MomSendKey"/> must differ between sends.
    /// </summary>
    public async Task SendMeetingMinutesAsync(VisitMailContext ctx)
    {
        var (subject, body) = await RenderAsync(MailContentKind.MeetingMinutes, ctx);

        if (!string.IsNullOrWhiteSpace(ctx.VisitorEmail))
        {
            if (MaySendVisitorCopy(ctx, "Minutes of meeting"))
                await _notifications.NotifyAsync("Email", ctx.VisitorEmail.Trim(), subject, body,
                    $"mom-visitor:{ctx.MomSendKey}", ctx.LocationId, HostSender(ctx));
        }
        else
        {
            _logger.LogWarning("Minutes of meeting visitor email skipped — {Name} has no email.", ctx.VisitorName);
        }

        if (!string.IsNullOrWhiteSpace(ctx.VisitingPersonEmail))
        {
            await _notifications.NotifyAsync("Email", ctx.VisitingPersonEmail.Trim(), subject, body,
                $"mom-host:{ctx.MomSendKey}", ctx.LocationId);
        }
        else
        {
            _logger.LogWarning("Minutes of meeting host email skipped — {Host} has no email.", ctx.VisitingPerson);
        }
    }

    public async Task SendAppointmentAsync(VisitMailContext ctx)
    {
        // The employee and the visitor get different wording, so each has its own Mail Content template.
        if (!string.IsNullOrWhiteSpace(ctx.VisitingPersonEmail))
        {
            var (subject, body) = await RenderAsync(MailContentKind.Appointment, ctx);
            await _notifications.NotifyAsync("Email", ctx.VisitingPersonEmail.Trim(), subject, body,
                $"appointment:{ctx.VisitNumber}{ctx.AppointmentSendKey}:{ctx.VisitingPersonEmail.Trim()}", ctx.LocationId);
        }
        else
        {
            _logger.LogWarning("Appointment email skipped — host {Host} has no email.", ctx.VisitingPerson);
        }

        if (!string.IsNullOrWhiteSpace(ctx.VisitorEmail))
        {
            if (!MaySendVisitorCopy(ctx, "Appointment")) return;
            var (subject, body) = await RenderAsync(MailContentKind.AppointmentVisitor, ctx);
            await _notifications.NotifyAsync("Email", ctx.VisitorEmail.Trim(), subject, body,
                $"appointment-visitor:{ctx.VisitNumber}{ctx.AppointmentSendKey}:{ctx.VisitorEmail.Trim()}", ctx.LocationId, HostSender(ctx));
        }
        else
        {
            _logger.LogWarning("Appointment visitor email skipped — {Name} has no email.", ctx.VisitorName);
        }
    }

    private async Task<(string Subject, string Body)> RenderAsync(MailContentKind kind, VisitMailContext ctx)
    {
        var candidates = await _db.MailContentTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && t.IsActive
                && (t.LocationId == null
                    || (ctx.LocationId != null && t.LocationId == ctx.LocationId)))
            .ToListAsync();

        var template = candidates.FirstOrDefault(t => ctx.LocationId != null && t.LocationId == ctx.LocationId)
            ?? candidates.FirstOrDefault(t => t.LocationId == null);

        string subject;
        string body;
        if (template is null)
        {
            var fallback = MasterDataService.DefaultMailContent(kind);
            subject = fallback.Subject;
            body = fallback.Body;
        }
        else
        {
            subject = template.Subject;
            body = template.Body;
        }

        if (kind is MailContentKind.Appointment or MailContentKind.AppointmentVisitor)
            body = EnsurePurposeLine(body);
        if (kind is MailContentKind.CheckIn && !string.IsNullOrWhiteSpace(ctx.MeetingHall))
            body = EnsureMeetingHallLine(body);
        if (kind is MailContentKind.CheckOut)
            body = RemoveMeetingMinutesSection(body);

        return (Apply(subject, ctx), Apply(body, ctx));
    }

    /// <summary>
    /// Minutes have their own email, so check-out templates saved when they carried the minutes lose the
    /// placeholder line and a "Minutes of meeting:" heading directly above it.
    /// </summary>
    public static string RemoveMeetingMinutesSection(string body)
    {
        if (!body.Contains("{{MeetingMinutes}}", StringComparison.Ordinal)) return body;

        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = body.Split(newline).ToList();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("{{MeetingMinutes}}", StringComparison.Ordinal)) continue;

            var remainder = lines[i].Replace("{{MeetingMinutes}}", "", StringComparison.Ordinal).Trim();
            var isLabelOnly = remainder.Length == 0 || remainder.EndsWith(':');
            if (!isLabelOnly)
            {
                lines[i] = lines[i].Replace("{{MeetingMinutes}}", "", StringComparison.Ordinal);
                continue;
            }

            lines.RemoveAt(i);
            if (remainder.Length == 0 && i > 0 && lines[i - 1].Trim().EndsWith(':')
                && lines[i - 1].Contains("minute", StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i - 1);
                i--;
            }
            if (i > 0 && i < lines.Count && lines[i - 1].Trim().Length == 0 && lines[i].Trim().Length == 0)
                lines.RemoveAt(i);
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    /// The host's check-in mail names the hall allocated at check-in: after the "Purpose:" or "Department:" line,
    /// or before the sign-off.
    /// </summary>
    public static string EnsureMeetingHallLine(string body)
    {
        if (body.Contains("{{MeetingHall}}", StringComparison.Ordinal)) return body;

        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = body.Split(newline).ToList();
        var anchor = lines.FindIndex(l => l.TrimStart().StartsWith("Purpose:", StringComparison.OrdinalIgnoreCase));
        if (anchor < 0)
            anchor = lines.FindIndex(l => l.TrimStart().StartsWith("Department:", StringComparison.OrdinalIgnoreCase));
        if (anchor >= 0)
        {
            var indent = lines[anchor][..(lines[anchor].Length - lines[anchor].TrimStart().Length)];
            lines.Insert(anchor + 1, $"{indent}Meeting hall: {{{{MeetingHall}}}}");
            return string.Join(newline, lines);
        }

        var signOff = lines.FindIndex(l => l.TrimStart().StartsWith("Regards", StringComparison.OrdinalIgnoreCase));
        if (signOff >= 0)
        {
            lines.Insert(signOff, "");
            lines.Insert(signOff, "Meeting hall: {{MeetingHall}}");
            return string.Join(newline, lines);
        }

        return body.TrimEnd() + newline + newline + "Meeting hall: {{MeetingHall}}" + newline;
    }

    /// <summary>
    /// Appointment mails must state the purpose. Templates saved before the placeholder existed get the line
    /// after "Department:", or before the sign-off when there is no department line.
    /// </summary>
    public static string EnsurePurposeLine(string body)
    {
        if (body.Contains("{{Purpose}}", StringComparison.Ordinal)) return body;

        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = body.Split(newline).ToList();
        var department = lines.FindIndex(l => l.TrimStart().StartsWith("Department:", StringComparison.OrdinalIgnoreCase));
        if (department >= 0)
        {
            var indent = lines[department][..(lines[department].Length - lines[department].TrimStart().Length)];
            lines.Insert(department + 1, $"{indent}Purpose: {{{{Purpose}}}}");
            return string.Join(newline, lines);
        }

        var signOff = lines.FindIndex(l => l.TrimStart().StartsWith("Regards", StringComparison.OrdinalIgnoreCase));
        if (signOff >= 0)
        {
            lines.Insert(signOff, "");
            lines.Insert(signOff, "Purpose: {{Purpose}}");
            return string.Join(newline, lines);
        }

        return body.TrimEnd() + newline + newline + "Purpose: {{Purpose}}" + newline;
    }

    private static string Apply(string template, VisitMailContext ctx)
    {
        var sb = new StringBuilder(template);
        sb.Replace("{{VisitorName}}", ctx.VisitorName);
        sb.Replace("{{Salutation}}", ctx.Salutation);
        sb.Replace("{{Company}}", ctx.Company);
        sb.Replace("{{VisitingPerson}}", ctx.VisitingPerson);
        sb.Replace("{{VisitorNumber}}", ctx.VisitorNumber);
        sb.Replace("{{VisitNumber}}", ctx.VisitNumber);
        sb.Replace("{{Location}}", ctx.Location);
        sb.Replace("{{Department}}", ctx.Department);
        sb.Replace("{{Purpose}}", ctx.Purpose);
        sb.Replace("{{MeetingHall}}", string.IsNullOrWhiteSpace(ctx.MeetingHall) ? "—" : ctx.MeetingHall);
        var discussion = string.IsNullOrWhiteSpace(ctx.MeetingMinutes) ? "—" : ctx.MeetingMinutes;
        sb.Replace("{{MeetingMinutes}}", discussion);
        sb.Replace("{{Discussion}}", discussion);
        sb.Replace("{{Decisions}}", string.IsNullOrWhiteSpace(ctx.MomDecisions) ? "—" : ctx.MomDecisions);
        sb.Replace("{{ActionItems}}", string.IsNullOrWhiteSpace(ctx.MomActionItems) ? "—" : ctx.MomActionItems);
        sb.Replace("{{NextMeetingDate}}", string.IsNullOrWhiteSpace(ctx.MomNextMeetingDate) ? "—" : ctx.MomNextMeetingDate);
        sb.Replace("{{Date}}", ctx.MomentLocal.ToString("dd MMM yyyy"));
        sb.Replace("{{Time}}", ctx.MomentLocal.ToString("hh:mm tt"));
        return sb.ToString();
    }
}
