using System.Text;

namespace Forge.Api.Jobs;

public static class EmailTemplateBuilder
{
    public static string BuildDigest(
        string companyName,
        string userName,
        List<DigestJobItem> upcomingJobs,
        List<DigestJobItem> overdueJobs,
        int completedYesterday)
    {
        var sb = new StringBuilder();
        sb.Append(WrapHtml(companyName, $"Daily Digest for {userName}", BuildDigestBody(
            userName, upcomingJobs, overdueJobs, completedYesterday)));
        return sb.ToString();
    }

    public static string BuildInvoiceEmail(
        string companyName,
        string customerName,
        string invoiceNumber,
        decimal total,
        DateTimeOffset? dueDate)
    {
        var body = $"""
            <p>Dear {Encode(customerName)},</p>
            <p>Please find attached invoice <strong>{Encode(invoiceNumber)}</strong>.</p>
            <table style="border-collapse:collapse;margin:16px 0;">
              <tr>
                <td style="padding:8px 16px;border:1px solid #ddd;font-weight:600;">Invoice #</td>
                <td style="padding:8px 16px;border:1px solid #ddd;">{Encode(invoiceNumber)}</td>
              </tr>
              <tr>
                <td style="padding:8px 16px;border:1px solid #ddd;font-weight:600;">Amount Due</td>
                <td style="padding:8px 16px;border:1px solid #ddd;">${total:N2}</td>
              </tr>
              {(dueDate.HasValue ? $"""
              <tr>
                <td style="padding:8px 16px;border:1px solid #ddd;font-weight:600;">Due Date</td>
                <td style="padding:8px 16px;border:1px solid #ddd;">{dueDate.Value:MMMM d, yyyy}</td>
              </tr>
              """ : "")}
            </table>
            <p>If you have any questions, please don't hesitate to reach out.</p>
            <p>Thank you for your business.</p>
            """;

        return WrapHtml(companyName, $"Invoice {invoiceNumber}", body);
    }

    public static string BuildNotificationEmail(
        string companyName,
        string recipientName,
        string notificationTitle,
        string notificationMessage)
    {
        var body = $"""
            <p>Hi {Encode(recipientName)},</p>
            <h3 style="margin:16px 0 8px;color:#333;">{Encode(notificationTitle)}</h3>
            <p style="color:#555;">{Encode(notificationMessage)}</p>
            <p style="margin-top:24px;">
              <a href="#" style="background:#2563eb;color:white;padding:10px 20px;text-decoration:none;font-weight:600;">
                Open {Encode(companyName)}
              </a>
            </p>
            """;

        return WrapHtml(companyName, notificationTitle, body);
    }

    private static string BuildDigestBody(
        string userName,
        List<DigestJobItem> upcomingJobs,
        List<DigestJobItem> overdueJobs,
        int completedYesterday)
    {
        var sb = new StringBuilder();
        sb.Append($"<p>Good morning, {Encode(userName)}! Here's your daily summary.</p>");

        if (completedYesterday > 0)
        {
            sb.Append($"""
                <div style="background:#ecfdf5;border-left:4px solid #10b981;padding:12px 16px;margin:16px 0;">
                  <strong style="color:#059669;">{completedYesterday} job{(completedYesterday != 1 ? "s" : "")} completed yesterday</strong>
                </div>
                """);
        }

        if (overdueJobs.Count > 0)
        {
            sb.Append("""<h3 style="color:#dc2626;margin:20px 0 8px;">⚠ Overdue Jobs</h3>""");
            sb.Append(BuildJobTable(overdueJobs, isOverdue: true));
        }

        if (upcomingJobs.Count > 0)
        {
            sb.Append("""<h3 style="color:#333;margin:20px 0 8px;">Upcoming (Next 3 Days)</h3>""");
            sb.Append(BuildJobTable(upcomingJobs, isOverdue: false));
        }

        if (overdueJobs.Count == 0 && upcomingJobs.Count == 0)
        {
            sb.Append("""
                <div style="background:#f0f9ff;border-left:4px solid #3b82f6;padding:12px 16px;margin:16px 0;">
                  <strong style="color:#2563eb;">No upcoming deadlines — great job staying on track!</strong>
                </div>
                """);
        }

        return sb.ToString();
    }

    private static string BuildJobTable(List<DigestJobItem> jobs, bool isOverdue)
    {
        var borderColor = isOverdue ? "#fecaca" : "#e5e7eb";
        var sb = new StringBuilder();
        sb.Append($"""<table style="border-collapse:collapse;width:100%;margin:8px 0;">""");
        sb.Append($"""
            <tr style="background:#f9fafb;">
              <th style="padding:8px 12px;border:1px solid {borderColor};text-align:left;font-size:11px;text-transform:uppercase;">Job #</th>
              <th style="padding:8px 12px;border:1px solid {borderColor};text-align:left;font-size:11px;text-transform:uppercase;">Title</th>
              <th style="padding:8px 12px;border:1px solid {borderColor};text-align:left;font-size:11px;text-transform:uppercase;">Due Date</th>
            </tr>
            """);

        foreach (var job in jobs)
        {
            var dateStr = job.DueDate?.ToString("MMM d, yyyy") ?? "—";
            sb.Append($"""
                <tr>
                  <td style="padding:8px 12px;border:1px solid {borderColor};font-weight:600;">{Encode(job.JobNumber)}</td>
                  <td style="padding:8px 12px;border:1px solid {borderColor};">{Encode(job.Title)}</td>
                  <td style="padding:8px 12px;border:1px solid {borderColor};{(isOverdue ? "color:#dc2626;font-weight:600;" : "")}">{dateStr}</td>
                </tr>
                """);
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static string WrapHtml(string companyName, string title, string body)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;font-family:'Segoe UI',Arial,sans-serif;background:#f5f5f5;">
              <div style="max-width:600px;margin:0 auto;background:white;">
                <div style="background:#1e293b;padding:20px 24px;">
                  <h1 style="margin:0;color:white;font-size:18px;font-weight:700;">{Encode(companyName)}</h1>
                </div>
                <div style="padding:24px;">
                  <h2 style="margin:0 0 16px;color:#1e293b;font-size:16px;">{Encode(title)}</h2>
                  {body}
                </div>
                <div style="background:#f9fafb;padding:16px 24px;border-top:1px solid #e5e7eb;text-align:center;">
                  <p style="margin:0;color:#9ca3af;font-size:11px;">
                    Sent by {Encode(companyName)} · <a href="#" style="color:#6b7280;">Manage preferences</a>
                  </p>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private static string Encode(string text) =>
        System.Net.WebUtility.HtmlEncode(text);
}
