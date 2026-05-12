using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetReportSchedulesQuery : IRequest<List<ReportScheduleResponseModel>>;

public class GetReportSchedulesHandler(AppDbContext db) : IRequestHandler<GetReportSchedulesQuery, List<ReportScheduleResponseModel>>
{
    public async Task<List<ReportScheduleResponseModel>> Handle(GetReportSchedulesQuery request, CancellationToken cancellationToken)
    {
        var schedules = await db.ReportSchedules
            .Include(s => s.SavedReport)
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return schedules.Select(s => new ReportScheduleResponseModel(
            s.Id,
            s.SavedReportId,
            s.SavedReport.Name,
            s.CronExpression,
            s.RecipientEmailsJson,
            s.Format,
            s.IsActive,
            s.LastSentAt,
            s.NextRunAt,
            s.SubjectTemplate,
            s.CreatedAt,
            s.UpdatedAt)).ToList();
    }
}
