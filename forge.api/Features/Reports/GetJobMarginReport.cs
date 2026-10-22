using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetJobMarginReportQuery(DateTimeOffset Start, DateTimeOffset End)
    : IRequest<List<JobMarginReportItem>>;

public class GetJobMarginReportHandler(AppDbContext db)
    : IRequestHandler<GetJobMarginReportQuery, List<JobMarginReportItem>>
{
    public async Task<List<JobMarginReportItem>> Handle(GetJobMarginReportQuery request, CancellationToken ct)
    {
        var rows = await JobMarginCalculator.LoadAsync(db, request.Start, request.End, ct);

        return rows
            .Select(r => new JobMarginReportItem(
                r.Job.JobNumber,
                r.Job.Title,
                r.Job.Customer?.Name,
                r.Revenue,
                r.LaborCost,
                r.MaterialCost,
                r.ExpenseCost,
                r.TotalCost,
                r.CostRecorded ? r.Margin : null,
                JobMarginCalculator.MarginPercentage(r),
                r.CostRecorded))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }
}
