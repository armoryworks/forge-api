using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Dashboard;

public record GetMarginSummaryQuery : IRequest<MarginSummaryResponseModel>;

public class GetMarginSummaryHandler(AppDbContext db)
    : IRequestHandler<GetMarginSummaryQuery, MarginSummaryResponseModel>
{
    private const decimal DefaultLaborRate = 75.00m;

    public async Task<MarginSummaryResponseModel> Handle(GetMarginSummaryQuery request, CancellationToken ct)
    {
        var laborRate = await GetLaborRateAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        var jobs = await db.Jobs
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
                        .ThenInclude(inv => inv.Lines)
            .Include(j => j.PurchaseOrders)
                .ThenInclude(po => po.Lines)
            .Where(j => j.CreatedAt >= cutoff)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return new MarginSummaryResponseModel(0, 0, 0, 0, 0);

        var jobIds = jobs.Select(j => j.Id).ToList();

        var timeByJob = await db.TimeEntries
            .Where(t => t.JobId.HasValue && jobIds.Contains(t.JobId.Value))
            .GroupBy(t => t.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalMinutes = g.Sum(t => t.DurationMinutes) })
            .ToDictionaryAsync(t => t.JobId, t => t.TotalMinutes, ct);

        var expensesByJob = await db.Expenses
            .Where(e => e.JobId.HasValue && jobIds.Contains(e.JobId.Value))
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalAmount = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(e => e.JobId, e => e.TotalAmount, ct);

        var totalRevenue = 0m;
        var totalCost = 0m;
        var marginPercentages = new List<decimal>();

        foreach (var job in jobs)
        {
            var revenue = 0m;
            if (job.SalesOrderLine?.SalesOrder?.Invoices is { Count: > 0 } invoices)
            {
                revenue = invoices.Sum(inv => inv.Lines.Sum(l => l.Quantity * l.UnitPrice));
            }

            var totalMinutes = timeByJob.GetValueOrDefault(job.Id, 0);
            var laborCost = Math.Round((decimal)totalMinutes / 60 * laborRate, 2);

            var materialCost = job.PurchaseOrders
                .SelectMany(po => po.Lines)
                .Sum(l => l.OrderedQuantity * l.UnitPrice);

            var expenseCost = expensesByJob.GetValueOrDefault(job.Id, 0m);
            var jobCost = laborCost + materialCost + expenseCost;

            totalRevenue += revenue;
            totalCost += jobCost;

            if (revenue > 0)
                marginPercentages.Add((revenue - jobCost) / revenue * 100);
        }

        var totalMargin = totalRevenue - totalCost;
        var avgMargin = marginPercentages.Count > 0
            ? Math.Round(marginPercentages.Average(), 1)
            : 0m;

        return new MarginSummaryResponseModel(totalRevenue, totalCost, totalMargin, avgMargin, jobs.Count);
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
