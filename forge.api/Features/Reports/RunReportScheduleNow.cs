using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reports;

public record RunReportScheduleNowCommand(int Id) : IRequest<int>;

public class RunReportScheduleNowHandler(
    AppDbContext db,
    ReportScheduleSender sender,
    IClock clock) : IRequestHandler<RunReportScheduleNowCommand, int>
{
    public async Task<int> Handle(RunReportScheduleNowCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.ReportSchedules
            .Include(s => s.SavedReport)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Report schedule {request.Id} not found.");

        var occurrenceKey = $"now-{clock.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture)}";
        var recipients = await sender.SendAsync(schedule, occurrenceKey, cancellationToken);

        db.LogActivityAt("sent", $"Sent scheduled report {schedule.SavedReport.Name} to {recipients} recipient(s)",
            ("ReportSchedule", schedule.Id), ("SavedReport", schedule.SavedReportId));
        await db.SaveChangesAsync(cancellationToken);

        return recipients;
    }
}
