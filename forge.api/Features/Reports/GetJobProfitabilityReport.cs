using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetJobProfitabilityReportQuery(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? CustomerId,
    decimal? MinMarginPercent) : IRequest<List<JobProfitabilityReportRow>>;

public class GetJobProfitabilityReportHandler(AppDbContext db)
    : IRequestHandler<GetJobProfitabilityReportQuery, List<JobProfitabilityReportRow>>
{
    public async Task<List<JobProfitabilityReportRow>> Handle(
        GetJobProfitabilityReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.Jobs.AsNoTracking().AsQueryable();

        if (request.CustomerId.HasValue)
            query = query.Where(j => j.CustomerId == request.CustomerId);

        if (request.DateFrom.HasValue)
        {
            var from = new DateTimeOffset(request.DateFrom.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(j => j.CompletedDate >= from);
        }

        if (request.DateTo.HasValue)
        {
            var to = new DateTimeOffset(request.DateTo.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(j => j.CompletedDate <= to);
        }

        // Only include jobs with a quoted price
        query = query.Where(j => j.QuotedPrice > 0);

        var jobs = await query
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                CustomerName = j.Customer != null ? j.Customer.Name : null,
                j.QuotedPrice,
                j.CompletedDate,
            })
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(j => j.Id).ToList();

        // Batch load material costs
        var materialCosts = await db.MaterialIssues
            .AsNoTracking()
            .Where(m => jobIds.Contains(m.JobId))
            .GroupBy(m => m.JobId)
            .Select(g => new
            {
                JobId = g.Key,
                Cost = g.Sum(m => m.IssueType == MaterialIssueType.Return
                    ? -(m.Quantity * m.UnitCost)
                    : m.Quantity * m.UnitCost),
            })
            .ToDictionaryAsync(x => x.JobId, x => x.Cost, cancellationToken);

        // Batch load labor + burden costs
        var laborBurdenCosts = await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId.HasValue && jobIds.Contains(t.JobId.Value))
            .GroupBy(t => t.JobId!.Value)
            .Select(g => new
            {
                JobId = g.Key,
                LaborCost = g.Sum(t => t.LaborCost),
                BurdenCost = g.Sum(t => t.BurdenCost),
            })
            .ToDictionaryAsync(x => x.JobId, cancellationToken);

        // Batch load subcontract costs
        var subcontractCosts = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PurchaseOrder.JobId.HasValue
                && jobIds.Contains(pol.PurchaseOrder.JobId.Value)
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled)
            .GroupBy(pol => pol.PurchaseOrder.JobId!.Value)
            .Select(g => new
            {
                JobId = g.Key,
                Cost = g.Sum(pol => pol.UnitPrice * pol.OrderedQuantity),
            })
            .ToDictionaryAsync(x => x.JobId, x => x.Cost, cancellationToken);

        var result = jobs.Select(j =>
        {
            var matCost = materialCosts.GetValueOrDefault(j.Id, 0m);
            var lb = laborBurdenCosts.GetValueOrDefault(j.Id);
            var labCost = lb?.LaborCost ?? 0m;
            var burCost = lb?.BurdenCost ?? 0m;
            var subCost = subcontractCosts.GetValueOrDefault(j.Id, 0m);
            var actualCost = matCost + labCost + burCost + subCost;
            var margin = j.QuotedPrice - actualCost;
            var marginPercent = j.QuotedPrice != 0 ? margin / j.QuotedPrice * 100 : 0m;

            return new JobProfitabilityReportRow
            {
                JobId = j.Id,
                JobNumber = j.JobNumber,
                JobTitle = j.Title,
                CustomerName = j.CustomerName,
                QuotedPrice = j.QuotedPrice,
                ActualCost = actualCost,
                Margin = margin,
                MarginPercent = marginPercent,
                MaterialCost = matCost,
                LaborCost = labCost,
                BurdenCost = burCost,
                SubcontractCost = subCost,
                CompletedAt = j.CompletedDate,
            };
        }).ToList();

        if (request.MinMarginPercent.HasValue)
            result = result.Where(r => r.MarginPercent >= request.MinMarginPercent.Value).ToList();

        return result.OrderByDescending(r => r.MarginPercent).ToList();
    }
}
