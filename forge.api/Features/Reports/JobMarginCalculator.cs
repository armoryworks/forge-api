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
            .Select(g => new { JobId = g.Key, TotalMinutes = g.Sum(t => t.DurationMinutes) })
            .ToDictionaryAsync(t => t.JobId, t => t.TotalMinutes, ct);

        var expensesByJob = await db.Expenses
            .Where(e => e.JobId.HasValue && jobIds.Contains(e.JobId.Value))
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalAmount = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(e => e.JobId, e => e.TotalAmount, ct);

        return jobs.Select(job => new JobMarginRow(
                job,
                InvoicedRevenue(job),
                Math.Round((decimal)timeByJob.GetValueOrDefault(job.Id, 0) / 60 * laborRate, 2),
                job.PurchaseOrders.SelectMany(po => po.Lines).Sum(l => l.OrderedQuantity * l.UnitPrice),
                expensesByJob.GetValueOrDefault(job.Id, 0m)))
            .ToList();
    }

    public static decimal WeightedMarginPercentage(IEnumerable<JobMarginRow> rows)
    {
        var measured = rows.Where(r => r.Revenue > 0 && r.TotalCost > 0).ToList();
        var revenue = measured.Sum(r => r.Revenue);
        return revenue > 0 ? Math.Round(measured.Sum(r => r.Margin) / revenue * 100, 1) : 0m;
    }

    private static decimal InvoicedRevenue(Job job)
    {
        if (job.SalesOrderLine is not { SalesOrder.Invoices: { Count: > 0 } invoices } soLine)
            return 0m;

        return invoices
            .Where(inv => inv.Status != InvoiceStatus.Voided && inv.Status != InvoiceStatus.Draft)
            .SelectMany(inv => inv.Lines)
            .Where(l => IsLineForSalesOrderLine(l, soLine))
            .Sum(l => l.Quantity * l.UnitPrice);
    }

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
