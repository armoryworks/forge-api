using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetJobMarginReportQuery(DateTimeOffset Start, DateTimeOffset End)
    : IRequest<List<JobMarginReportItem>>;

public class GetJobMarginReportHandler(AppDbContext db)
    : IRequestHandler<GetJobMarginReportQuery, List<JobMarginReportItem>>
{
    private const decimal DefaultLaborRate = 75.00m;

    public async Task<List<JobMarginReportItem>> Handle(GetJobMarginReportQuery request, CancellationToken ct)
    {
        var laborRate = await GetLaborRateAsync(ct);

        var jobs = await db.Jobs
            .Include(j => j.Customer)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
                        .ThenInclude(inv => inv.Lines)
            .Include(j => j.PurchaseOrders)
                .ThenInclude(po => po.Lines)
            .Where(j => j.CreatedAt >= request.Start.UtcDateTime && j.CreatedAt <= request.End.UtcDateTime)
            .AsSplitQuery()
            .ToListAsync(ct);

        var jobIds = jobs.Select(j => j.Id).ToList();

        // Time entries linked to these jobs
        var timeEntries = await db.TimeEntries
            .Where(t => t.JobId.HasValue && jobIds.Contains(t.JobId.Value))
            .GroupBy(t => t.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalMinutes = g.Sum(t => t.DurationMinutes) })
            .ToListAsync(ct);
        var timeByJob = timeEntries.ToDictionary(t => t.JobId, t => t.TotalMinutes);

        // Expenses linked to these jobs
        var expenses = await db.Expenses
            .Where(e => e.JobId.HasValue && jobIds.Contains(e.JobId.Value))
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalAmount = g.Sum(e => e.Amount) })
            .ToListAsync(ct);
        var expensesByJob = expenses.ToDictionary(e => e.JobId, e => e.TotalAmount);

        var results = new List<JobMarginReportItem>();

        foreach (var job in jobs)
        {
            // Revenue: sum of invoice line totals linked via SalesOrder
            var revenue = 0m;
            if (job.SalesOrderLine?.SalesOrder?.Invoices is { Count: > 0 } invoices)
            {
                revenue = invoices.Sum(inv => inv.Lines.Sum(l => l.Quantity * l.UnitPrice));
            }

            // Labor cost
            var totalMinutes = timeByJob.GetValueOrDefault(job.Id, 0);
            var laborCost = Math.Round((decimal)totalMinutes / 60 * laborRate, 2);

            // Material cost (PO line totals)
            var materialCost = job.PurchaseOrders
                .SelectMany(po => po.Lines)
                .Sum(l => l.OrderedQuantity * l.UnitPrice);

            // Expense cost
            var expenseCost = expensesByJob.GetValueOrDefault(job.Id, 0m);

            var totalCost = laborCost + materialCost + expenseCost;
            var margin = revenue - totalCost;
            var marginPercentage = revenue > 0 ? Math.Round(margin / revenue * 100, 1) : 0m;

            results.Add(new JobMarginReportItem(
                job.JobNumber,
                job.Title,
                job.Customer?.Name,
                revenue,
                laborCost,
                materialCost,
                expenseCost,
                totalCost,
                margin,
                marginPercentage));
        }

        return results.OrderByDescending(r => r.Revenue).ToList();
    }

    private async Task<decimal> GetLaborRateAsync(CancellationToken ct)
    {
        var setting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "LaborRate", ct);

        return setting is not null && decimal.TryParse(setting.Value, out var rate)
            ? rate
            : DefaultLaborRate;
    }
}
