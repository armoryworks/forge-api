using System.Text;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ExportJobsCalendarQuery(int? AssigneeId, int? TrackTypeId) : IRequest<byte[]>;

public class ExportJobsCalendarHandler(AppDbContext db) : IRequestHandler<ExportJobsCalendarQuery, byte[]>
{
    public async Task<byte[]> Handle(ExportJobsCalendarQuery request, CancellationToken ct)
    {
        var query = db.Jobs
            .Include(j => j.Customer)
            .Include(j => j.CurrentStage)
            .Where(j => j.DueDate.HasValue);

        if (request.AssigneeId.HasValue)
            query = query.Where(j => j.AssigneeId == request.AssigneeId);

        if (request.TrackTypeId.HasValue)
            query = query.Where(j => j.TrackTypeId == request.TrackTypeId);

        var jobs = await query.OrderBy(j => j.DueDate).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//QB Engineer//Job Calendar//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");

        foreach (var job in jobs)
        {
            var dueDate = job.DueDate!.Value;
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:job-{job.Id}@forge");
            sb.AppendLine($"DTSTART;VALUE=DATE:{dueDate:yyyyMMdd}");
            sb.AppendLine($"DTEND;VALUE=DATE:{dueDate.AddDays(1):yyyyMMdd}");
            sb.AppendLine($"SUMMARY:{EscapeIcs(job.Title)}");

            var desc = $"Job #{job.Id}";
            if (job.Customer != null)
                desc += $"\\nCustomer: {job.Customer.Name}";
            if (job.CurrentStage != null)
                desc += $"\\nStage: {job.CurrentStage.Name}";
            if (!string.IsNullOrEmpty(job.Priority.ToString()))
                desc += $"\\nPriority: {job.Priority}";
            sb.AppendLine($"DESCRIPTION:{EscapeIcs(desc)}");

            sb.AppendLine($"DTSTAMP:{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeIcs(string value) =>
        value.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\n", "\\n");
}
