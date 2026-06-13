using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class JobCostService(AppDbContext db) : IJobCostService
{
    public async Task<JobCostSummaryModel> GetCostSummaryAsync(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.QuotedPrice,
                j.EstimatedMaterialCost,
                j.EstimatedLaborCost,
                j.EstimatedBurdenCost,
                j.EstimatedSubcontractCost,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found");

        var materialActual = await GetActualMaterialCostAsync(jobId, ct);
        var laborActual = await GetActualLaborCostAsync(jobId, ct);
        var burdenActual = await GetActualBurdenCostAsync(jobId, ct);
        var subcontractActual = await GetActualSubcontractCostAsync(jobId, ct);

        return new JobCostSummaryModel
        {
            JobId = job.Id,
            JobNumber = job.JobNumber,
            QuotedPrice = job.QuotedPrice,
            MaterialEstimated = job.EstimatedMaterialCost,
            MaterialActual = materialActual,
            LaborEstimated = job.EstimatedLaborCost,
            LaborActual = laborActual,
            BurdenEstimated = job.EstimatedBurdenCost,
            BurdenActual = burdenActual,
            SubcontractEstimated = job.EstimatedSubcontractCost,
            SubcontractActual = subcontractActual,
        };
    }

    public async Task<decimal> GetActualMaterialCostAsync(int jobId, CancellationToken ct)
    {
        return await db.MaterialIssues
            .AsNoTracking()
            .Where(m => m.JobId == jobId)
            .SumAsync(m => m.IssueType == MaterialIssueType.Return
                ? -(m.Quantity * m.UnitCost)
                : m.Quantity * m.UnitCost, ct);
    }

    public async Task<decimal> GetActualLaborCostAsync(int jobId, CancellationToken ct)
    {
        return await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .SumAsync(t => t.LaborCost, ct);
    }

    public async Task<decimal> GetActualLaborCostAtActualRateAsync(int jobId, CancellationToken ct)
    {
        return await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            // ActualLaborCost == 0 means no actual rate was applied yet → use the standard-rate LaborCost.
            .SumAsync(t => t.ActualLaborCost != 0m ? t.ActualLaborCost : t.LaborCost, ct);
    }

    public async Task<decimal> GetLaborRateVarianceAsync(int jobId, CancellationToken ct)
    {
        return await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ActualLaborCost != 0m)
            .SumAsync(t => t.ActualLaborCost - t.LaborCost, ct);
    }

    public async Task<decimal> GetActualBurdenCostAsync(int jobId, CancellationToken ct)
    {
        return await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId)
            .SumAsync(t => t.BurdenCost, ct);
    }

    public async Task<decimal> GetActualSubcontractCostAsync(int jobId, CancellationToken ct)
    {
        return await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PurchaseOrder.JobId == jobId
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled)
            .Join(db.Operations.Where(o => o.IsSubcontract),
                pol => pol.PartId,
                op => op.PartId,
                (pol, op) => pol.UnitPrice * pol.OrderedQuantity)
            .SumAsync(ct);
    }

    public async Task<decimal> GetCurrentLaborRateAsync(int userId, DateTimeOffset asOf, CancellationToken ct)
    {
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);

        var rate = await db.LaborRates
            .AsNoTracking()
            .Where(r => r.UserId == userId
                && r.EffectiveFrom <= asOfDate
                && (r.EffectiveTo == null || r.EffectiveTo >= asOfDate))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        return rate?.StandardRatePerHour ?? 0m;
    }

    public async Task RecalculateTimeEntryCostsAsync(int jobId, CancellationToken ct)
    {
        var entries = await db.TimeEntries
            .Where(t => t.JobId == jobId)
            .Include(t => t.Operation)
            .ToListAsync(ct);

        // Pre-load labor rates for all users referenced by these entries
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var laborRates = await db.LaborRates
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .OrderByDescending(r => r.EffectiveFrom)
            .ToListAsync(ct);

        var ratesByUser = laborRates
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entry in entries)
        {
            var entryDate = DateOnly.FromDateTime(entry.Date.ToDateTime(TimeOnly.MinValue));

            var userRate = ratesByUser.TryGetValue(entry.UserId, out var rates)
                ? rates.FirstOrDefault(r => r.EffectiveFrom <= entryDate
                    && (r.EffectiveTo == null || r.EffectiveTo >= entryDate))
                : null;

            var hourlyRate = userRate?.StandardRatePerHour ?? 0m;
            var actualRate = userRate?.ActualRatePerHour ?? hourlyRate; // no actual rate → costs at standard
            var burdenRate = entry.Operation?.BurdenRate ?? 0m;
            var hours = entry.DurationMinutes / 60m;

            entry.LaborCost = hours * hourlyRate;       // standard-rate basis (efficiency variance)
            entry.ActualLaborCost = hours * actualRate; // actual-rate basis (rate variance = actual − standard)
            entry.BurdenCost = hours * burdenRate;
        }

        await db.SaveChangesAsync(ct);
    }
}
