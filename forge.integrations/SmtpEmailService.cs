using MailKit.Net.Smtp;

using Microsoft.Extensions.Logging;

using MimeKit;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Integrations;

/// <summary>
/// Real implementation of <see cref="IEmailService"/> backed by MailKit's
/// <see cref="SmtpClient"/>. Phase 1m: host/port/credentials/from-fields
/// read live from <see cref="ISettingsService"/> at send time. Each
/// SendAsync opens a fresh SMTP connection (matches pre-1m behaviour
/// which constructed a new <c>SmtpClient</c> per send anyway).
/// </summary>
public class SmtpEmailService(ISettingsService settings, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var s = await ReadAsync(ct);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder { HtmlBody = message.HtmlBody };

        if (!string.IsNullOrEmpty(message.PlainTextBody))
            builder.TextBody = message.PlainTextBody;

        if (message.Attachments != null)
        {
            foreach (var attachment in message.Attachments)
            {
                builder.Attachments.Add(attachment.FileName, attachment.Content,
                    ContentType.Parse(attachment.ContentType));
            }
        }

        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(s.Host, s.Port, s.UseSsl, ct);

        if (!string.IsNullOrEmpty(s.Username))
            await client.AuthenticateAsync(s.Username, s.Password ?? string.Empty, ct);

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("Email sent to {To}: {Subject}", message.To, message.Subject);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var s = await ReadAsync(ct);
            using var client = new SmtpClient();
            await client.ConnectAsync(s.Host, s.Port, s.UseSsl, ct);

            if (!string.IsNullOrEmpty(s.Username))
                await client.AuthenticateAsync(s.Username, s.Password ?? string.Empty, ct);

            await client.DisconnectAsync(true, ct);
            logger.LogInformation("[SMTP] Connection test succeeded");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SMTP] Connection test failed");
            return false;
        }
    }

    private async Task<SmtpResolved> ReadAsync(CancellationToken ct)
    {
        var host = await settings.GetStringAsync(SmtpSettings.KeyHost, ct);
        var portStr = await settings.GetStringAsync(SmtpSettings.KeyPort, ct);
        var useSslStr = await settings.GetStringAsync(SmtpSettings.KeyUseSsl, ct);
        var username = await settings.GetStringAsync(SmtpSettings.KeyUsername, ct);
        var password = await settings.GetStringAsync(SmtpSettings.KeyPassword, ct);
        var fromAddress = await settings.GetStringAsync(SmtpSettings.KeyFromAddress, ct);
        var fromName = await settings.GetStringAsync(SmtpSettings.KeyFromName, ct);

        if (string.IsNullOrEmpty(host))
        {
            throw new InvalidOperationException(
                "SMTP is not configured. Set host/port + credentials under Admin → Integrations → SMTP.");
        }

        var port = int.TryParse(portStr, out var p) ? p : 587;
        var useSsl = bool.TryParse(useSslStr, out var ssl) ? ssl : true;

        return new SmtpResolved(
            Host: host,
            Port: port,
            UseSsl: useSsl,
            Username: username,
            Password: password,
            FromAddress: fromAddress ?? "noreply@forge.local",
            FromName: fromName ?? "QB Engineer");
    }

    private sealed record SmtpResolved(
        string Host,
        int Port,
        bool UseSsl,
        string? Username,
        string? Password,
        string FromAddress,
        string FromName);
}
