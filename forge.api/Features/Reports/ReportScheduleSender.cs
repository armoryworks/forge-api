using System.Net;

using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public class ReportScheduleSender(
    AppDbContext db,
    IMediator mediator,
    IIntegrationOutboxService outbox,
    IClock clock)
{
    public async Task<int> SendAsync(ReportSchedule schedule, string occurrenceKey, CancellationToken ct)
    {
        var report = schedule.SavedReport
            ?? await db.SavedReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == schedule.SavedReportId, ct)
            ?? throw new KeyNotFoundException($"Saved report {schedule.SavedReportId} not found.");

        var recipients = ReportScheduleRecipients.Parse(schedule.RecipientEmailsJson);
        if (recipients.Length == 0) return 0;

        var export = await mediator.Send(new ExportReportQuery(schedule.SavedReportId, schedule.Format), ct);
        var subject = string.IsNullOrWhiteSpace(schedule.SubjectTemplate) ? report.Name : schedule.SubjectTemplate.Trim();
        var encodedName = WebUtility.HtmlEncode(report.Name);
        var html = $"<p>The scheduled report <strong>{encodedName}</strong> is attached ({WebUtility.HtmlEncode(export.FileName)}).</p>";
        var text = $"The scheduled report {report.Name} is attached ({export.FileName}).";

        foreach (var recipient in recipients)
        {
            await outbox.EnqueueEmailAsync(
                $"report-schedule:{schedule.Id}:{occurrenceKey}:{recipient.ToLowerInvariant()}",
                new EmailMessage(
                    recipient,
                    subject,
                    html,
                    text,
                    [new EmailAttachment(export.FileName, export.ContentType, export.Content)]),
                "ReportSchedule",
                schedule.Id,
                ct);
        }

        schedule.LastSentAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return recipients.Length;
    }

    public async Task<DateTimeOffset?> NextRunAfterAsync(string cronExpression, DateTimeOffset afterUtc, CancellationToken ct)
    {
        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        return ReportScheduleCron.NextOccurrence(cronExpression, afterUtc, zone);
    }
}
