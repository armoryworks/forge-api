using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class JobCostService(
    AppDbContext db, ICapabilitySnapshotProvider? capabilities = null, IStandardCostResolver? standardCost = null) : IJobCostService
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
                j.PartId,
                BuildQuantity = j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => (decimal?)jp.Quantity) ?? 0m,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found");

        var estimate = new JobEstimate(
            job.EstimatedMaterialCost, job.EstimatedLaborCost, job.EstimatedBurdenCost, job.EstimatedSubcontractCost);
        if (standardCost is not null && JobEstimates.IsUnset(estimate.Material, estimate.Labor, estimate.Burden, estimate.Subcontract))
            estimate = await JobEstimates.ComputeAsync(db, standardCost, job.PartId, job.BuildQuantity, ct);

        var materialActual = await GetActualMaterialCostAsync(jobId, ct);
        var laborActual = await GetActualLaborCostAsync(jobId, ct);
        var burdenActual = await GetActualBurdenCostAsync(jobId, ct);
        var subcontractActual = await GetActualSubcontractCostAsync(jobId, ct);
        var prices = await JobSalesPrice.ResolveAsync(db, [jobId], ct);

        return new JobCostSummaryModel
        {
            JobId = job.Id,
            JobNumber = job.JobNumber,
            QuotedPrice = prices.GetValueOrDefault(jobId, job.QuotedPrice),
            MaterialEstimated = estimate.Material,
            MaterialActual = materialActual,
            LaborEstimated = estimate.Labor,
            LaborActual = laborActual,
            BurdenEstimated = estimate.Burden,
            BurdenActual = burdenActual,
            SubcontractEstimated = estimate.Subcontract,
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
                op => (int?)op.PartId,
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
            .ToListAsync(ct);

        await PriceAsync(entries, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task PriceUnpricedTimeEntriesAsync(int jobId, CancellationToken ct)
    {
        var entries = await db.TimeEntries
            .Where(t => t.JobId == jobId && t.DurationMinutes > 0
                && t.LaborCost == 0m && t.ActualLaborCost == 0m && t.BurdenCost == 0m)
            .ToListAsync(ct);
        if (entries.Count == 0)
            return;

        await PriceAsync(entries, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task PriceTimeEntryAsync(TimeEntry entry, CancellationToken ct) => PriceAsync([entry], ct);

    private async Task PriceAsync(IReadOnlyList<TimeEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
            return;

        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var ratesByUser = (await db.LaborRates
                .AsNoTracking()
                .Where(r => userIds.Contains(r.UserId))
                .OrderByDescending(r => r.EffectiveFrom)
                .ToListAsync(ct))
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var operationIds = entries.Where(e => e.OperationId.HasValue).Select(e => e.OperationId!.Value).Distinct().ToList();
        var operations = await db.Operations
            .AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .Select(o => new { o.Id, o.WorkCenterId, o.LaborRate, o.BurdenRate })
            .ToDictionaryAsync(o => o.Id, ct);

        int? WorkCenterOf(TimeEntry e) =>
            e.WorkCenterId ?? (e.OperationId is int opId && operations.TryGetValue(opId, out var op) ? op.WorkCenterId : null);

        var workCenterIds = entries.Select(WorkCenterOf).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var workCenters = await db.WorkCenters
            .AsNoTracking()
            .Where(w => workCenterIds.Contains(w.Id))
            .Select(w => new { w.Id, w.LaborCostPerHour, w.BurdenRatePerHour, w.BurdenBasis })
            .ToDictionaryAsync(w => w.Id, ct);

        var policies = new Dictionary<DateOnly, CostingConversionPolicy>();
        foreach (var entry in entries)
        {
            if (!policies.TryGetValue(entry.Date, out var policy))
            {
                policy = await CostingConversionPolicy.LoadAsync(db, capabilities, entry.Date, ct);
                policies[entry.Date] = policy;
            }

            var userRate = ratesByUser.TryGetValue(entry.UserId, out var rates)
                ? rates.FirstOrDefault(r => r.EffectiveFrom <= entry.Date
                    && (r.EffectiveTo == null || r.EffectiveTo >= entry.Date))
                : null;
            var operation = entry.OperationId is int operationId ? operations.GetValueOrDefault(operationId) : null;
            var workCenterId = WorkCenterOf(entry);
            var workCenter = workCenterId is int wcId ? workCenters.GetValueOrDefault(wcId) : null;

            var hourlyRate = userRate?.StandardRatePerHour
                ?? Positive(workCenter?.LaborCostPerHour)
                ?? Positive(operation?.LaborRate)
                ?? 0m;
            var actualRate = userRate?.ActualRatePerHour ?? hourlyRate; // no actual rate → costs at standard
            var burdenPerHour = Positive(operation?.BurdenRate) ?? workCenter?.BurdenRatePerHour ?? 0m;
            var laborHours = (entry.LaborMinutes ?? entry.DurationMinutes) / 60m;
            var machineHours = entry.DurationMinutes / 60m;

            entry.LaborCost = laborHours * hourlyRate;       // standard-rate basis (efficiency variance)
            entry.ActualLaborCost = laborHours * actualRate; // actual-rate basis (rate variance = actual − standard)
            var machineBasis = workCenter?.BurdenBasis == BurdenBasis.Machine;
            entry.BurdenCost = machineBasis
                ? policy.Overhead(workCenterId, machineHours, machineHours * hourlyRate, burdenPerHour)
                : policy.Overhead(workCenterId, laborHours, entry.LaborCost, burdenPerHour);
        }
    }

    private static decimal? Positive(decimal? value) => value > 0m ? value : null;
}
