using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public sealed record JobMarginRow(
    Job Job,
    decimal Revenue,
    decimal LaborCost,
    decimal MaterialCost,
    decimal ExpenseCost)
{
    public decimal TotalCost => LaborCost + MaterialCost + ExpenseCost;
    public bool CostRecorded => TotalCost > 0;
    public bool Measured => CostRecorded && Revenue > 0;
    public decimal Margin => Revenue - TotalCost;
}

public static class JobMarginCalculator
{
    private const decimal DefaultLaborRate = 75.00m;

    public static async Task<List<JobMarginRow>> LoadAsync(
        AppDbContext db, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct)
    {
        var laborRate = await GetLaborRateAsync(db, ct);

        var query = db.Jobs.Where(j => j.CreatedAt >= start.UtcDateTime);
        if (end is { } until)
            query = query.Where(j => j.CreatedAt <= until.UtcDateTime);

        var jobs = await query
            .Include(j => j.Customer)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
                        .ThenInclude(inv => inv.Lines)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Lines)
            .Include(j => j.PurchaseOrders)
                .ThenInclude(po => po.Lines)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return [];

        var jobIds = jobs.Select(j => j.Id).ToList();

        var timeByJob = await db.TimeEntries
            .Where(t => t.JobId.HasValue && jobIds.Contains(t.JobId.Value))
            .GroupBy(t => t.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalMinutes = g.Sum(t => t.LaborMinutes ?? t.DurationMinutes) })
            .ToDictionaryAsync(t => t.JobId, t => t.TotalMinutes, ct);

        var expensesByJob = await db.Expenses
            .Where(e => e.JobId.HasValue && jobIds.Contains(e.JobId.Value))
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalAmount = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(e => e.JobId, e => e.TotalAmount, ct);

        var revenueByOrder = new Dictionary<int, Dictionary<int, decimal>>();

        return jobs.Select(job => new JobMarginRow(
                job,
                InvoicedRevenue(job, revenueByOrder),
                Math.Round(timeByJob.GetValueOrDefault(job.Id, 0m) / 60 * laborRate, 2),
                Math.Round(job.PurchaseOrders.Sum(po => po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice) * (po.FxRate ?? 1m)), 2),
                expensesByJob.GetValueOrDefault(job.Id, 0m)))
            .ToList();
    }

    public static decimal? MarginPercentage(JobMarginRow row) =>
        row.Measured ? Math.Round(row.Margin / row.Revenue * 100, 1) : null;

    public static decimal MeasuredMargin(IEnumerable<JobMarginRow> rows) =>
        rows.Where(r => r.Measured).Sum(r => r.Margin);

    public static decimal WeightedMarginPercentage(IEnumerable<JobMarginRow> rows)
    {
        var measured = rows.Where(r => r.Measured).ToList();
        var revenue = measured.Sum(r => r.Revenue);
        return revenue > 0 ? Math.Round(measured.Sum(r => r.Margin) / revenue * 100, 1) : 0m;
    }

    private static decimal InvoicedRevenue(Job job, Dictionary<int, Dictionary<int, decimal>> revenueByOrder)
    {
        if (job.SalesOrderLine is not { SalesOrder: { } order } soLine)
            return 0m;

        if (!revenueByOrder.TryGetValue(order.Id, out var byLine))
        {
            byLine = AllocateRevenue(order);
            revenueByOrder[order.Id] = byLine;
        }

        return Math.Round(byLine.GetValueOrDefault(soLine.Id), 2, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<int, decimal> AllocateRevenue(SalesOrder order)
    {
        var revenue = new Dictionary<int, decimal>();
        var allocatedQty = new Dictionary<int, decimal>();
        var orderLines = order.Lines.OrderBy(l => l.LineNumber).ThenBy(l => l.Id).ToList();
        var orderLineIds = orderLines.Select(l => l.Id).ToHashSet();

        var billed = order.Invoices
            .Where(inv => inv.Status != InvoiceStatus.Voided && inv.Status != InvoiceStatus.Draft)
            .OrderBy(inv => inv.InvoiceDate).ThenBy(inv => inv.Id)
            .SelectMany(inv => inv.Lines.Where(l => !l.IsFreight).OrderBy(l => l.LineNumber).Select(l => (Line: l, Rate: inv.FxRate)))
            .ToList();

        foreach (var (line, rate) in billed.Where(b => b.Line.SalesOrderLineId is int id && orderLineIds.Contains(id)))
        {
            var id = line.SalesOrderLineId!.Value;
            Add(revenue, id, line.Quantity * line.UnitPrice * rate);
            Add(allocatedQty, id, line.Quantity);
        }

        foreach (var (line, rate) in billed.Where(b => b.Line.SalesOrderLineId is not int id || !orderLineIds.Contains(id)))
        {
            var matches = orderLines.Where(ol => IsLineForSalesOrderLine(line, ol)).ToList();
            if (matches.Count == 0)
                continue;

            var remaining = line.Quantity;
            for (var i = 0; i < matches.Count && remaining > 0; i++)
            {
                var target = matches[i];
                var isLast = i == matches.Count - 1;
                var room = Math.Max(0m, target.Quantity - allocatedQty.GetValueOrDefault(target.Id));
                var take = isLast ? remaining : Math.Min(room, remaining);
                if (take <= 0)
                    continue;
                Add(revenue, target.Id, take * line.UnitPrice * rate);
                Add(allocatedQty, target.Id, take);
                remaining -= take;
            }
        }

        return revenue;
    }

    private static void Add(Dictionary<int, decimal> totals, int key, decimal amount) =>
        totals[key] = totals.GetValueOrDefault(key) + amount;

    private static bool IsLineForSalesOrderLine(InvoiceLine invoiceLine, SalesOrderLine salesOrderLine) =>
        salesOrderLine.PartId is { } partId
            ? invoiceLine.PartId == partId
            : invoiceLine.PartId is null
                && string.Equals(invoiceLine.Description.Trim(), salesOrderLine.Description.Trim(), StringComparison.OrdinalIgnoreCase);

    private static async Task<decimal> GetLaborRateAsync(AppDbContext db, CancellationToken ct)
    {
        var setting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "LaborRate", ct);

        return setting is not null && decimal.TryParse(setting.Value, out var rate)
            ? rate
            : DefaultLaborRate;
    }
}
