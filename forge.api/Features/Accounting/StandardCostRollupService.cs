using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Computes a part's standard unit cost decomposed into material / labor / overhead from the captured master
/// data — the "cost rollup" the variance decomposition needs (the D5 cost-recalc engine, used live by
/// <see cref="StandardCostResolver"/> now and available to persist into <c>CostCalculation</c> later):
/// <list type="bullet">
///   <item><b>Labor</b> = Σ operations <c>(EstimatedMs/3600000) × LaborAttendancePercent/100 × WorkCenter.LaborCostPerHour</c>
///         (an operator who only tends a machine cycle is costed for the share of it he attends).</item>
///   <item><b>Overhead (flat, Tier 1)</b> = Σ operations <c>hours × WorkCenter.BurdenRatePerHour</c>
///         (work-center burden rate × hours), where hours are the full operation hours on a Machine-basis work
///         center and the attended labor hours otherwise, the same basis actual time entries are priced on.</item>
///   <item><b>Overhead (departmental, Tier 2)</b> = Σ operations <c>opLaborCost × ratePct(WorkCenter)/100</c> —
///         a per-work-center percentage of direct labor from the active <see cref="CostingProfile"/> in
///         <c>departmental</c> mode; work centers with no configured rate fall back to the flat burden rate.
///         Only applied when <c>CAP-COSTING-TIER2-DEPTRATES</c> is enabled.</item>
///   <item><b>Frozen period rates (Tier 3)</b> — when <c>CAP-COSTING-TIER3-ABC</c> is enabled and a frozen costing
///         period covers the roll date, each work center's rates come from that period's
///         <see cref="WorkCenterCostRate"/> row instead: labor = attended hours × LaborRate; overhead = attended
///         hours × LaborOhRate + burden-basis hours × MachineRate + full hours × (MachineOhVarRate +
///         MachineOhFixedRate). A work center with no frozen row keeps its current rates. The result records
///         the source in <see cref="StandardCostElements.RateSource"/>.</item>
///   <item><b>Material</b> = Σ BOM lines <c>Quantity × child standard cost</c>, recursive for sub-assemblies
///         (a child's standard is its own rollup, or its ManualCostOverride for a purchased leaf).</item>
/// </list>
/// Cycle-guarded against BOM loops. Subcontract operations are excluded (their cost lands via the PO receipt).
/// </summary>
public interface IStandardCostRollupService
{
    Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class StandardCostRollupService(
    AppDbContext db, IClock clock, ICapabilitySnapshotProvider capabilities) : IStandardCostRollupService
{
    private const int MaxDepth = 32; // defensive bound on BOM depth (cycle guard already prevents true loops)

    public const string Tier3Capability = "CAP-COSTING-TIER3-ABC";

    private sealed record FrozenRates(int CostingPeriodId, IReadOnlyDictionary<int, WorkCenterCostRate> ByWorkCenter);

    public async Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        var policy = await CostingConversionPolicy.LoadAsync(db, capabilities, DateOnly.FromDateTime(now), ct);
        var frozen = await LoadFrozenRatesAsync(now, ct);
        var rolled = await RollupAsync(partId, policy, frozen, new HashSet<int>(), 0, ct);
        return frozen is null
            ? rolled
            : rolled with { RateSource = StandardCostRateSource.FrozenPeriod, CostingPeriodId = frozen.CostingPeriodId };
    }

    private async Task<FrozenRates?> LoadFrozenRatesAsync(DateTime now, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(Tier3Capability))
            return null;

        var today = now.Date;
        var periodId = await db.CostingPeriods.AsNoTracking()
            .Where(p => (p.Status == CostingPeriodStatus.Frozen || p.Status == CostingPeriodStatus.Closed)
                && p.FrozenAt != null && p.StartDate <= now && p.EndDate >= today)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (periodId is not int id)
            return null;

        var rates = await db.WorkCenterCostRates.AsNoTracking()
            .Where(r => r.CostingPeriodId == id)
            .ToListAsync(ct);
        return new FrozenRates(id, rates
            .GroupBy(r => r.WorkCenterId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First()));
    }

    private async Task<StandardCostElements> RollupAsync(
        int partId, CostingConversionPolicy policy, FrozenRates? frozen, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth || !visited.Add(partId))
            return StandardCostElements.Zero; // cycle / runaway guard

        try
        {
            var (labor, overhead) = await RollupConversionAsync(partId, policy, frozen, ct);

            // Material from the live part BOM (BOMLine: parent → child × qty), recursive.
            var bomLines = await db.BOMLines.AsNoTracking()
                .Where(b => b.ParentPartId == partId)
                .Select(b => new { b.ChildPartId, b.Quantity })
                .ToListAsync(ct);

            decimal material = 0m;
            foreach (var line in bomLines)
            {
                var child = await RollupAsync(line.ChildPartId, policy, frozen, visited, depth + 1, ct);
                var childUnit = child.Total;
                if (childUnit <= 0m)
                    // Leaf / purchased child with no rollup — fall back to its manual standard.
                    childUnit = await db.Parts.AsNoTracking()
                        .Where(p => p.Id == line.ChildPartId)
                        .Select(p => p.ManualCostOverride ?? 0m)
                        .FirstOrDefaultAsync(ct);
                material += line.Quantity * childUnit;
            }

            return new StandardCostElements(
                decimal.Round(material, 4), decimal.Round(labor, 4), decimal.Round(overhead, 4));
        }
        finally
        {
            visited.Remove(partId);
        }
    }

    /// <summary>Labor + overhead from the part's routing (operations × time × work-center rates).</summary>
    private async Task<(decimal Labor, decimal Overhead)> RollupConversionAsync(
        int partId, CostingConversionPolicy policy, FrozenRates? frozen, CancellationToken ct)
    {
        var ops = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId && !o.IsSubcontract && o.WorkCenterId != null && o.EstimatedMs != null)
            .Select(o => new { Ms = o.EstimatedMs!.Value, WorkCenterId = o.WorkCenterId!.Value, o.LaborAttendancePercent })
            .ToListAsync(ct);
        if (ops.Count == 0)
            return (0m, 0m);

        var wcIds = ops.Select(o => o.WorkCenterId).Distinct().ToList();
        var rates = await db.WorkCenters.AsNoTracking()
            .Where(w => wcIds.Contains(w.Id))
            .Select(w => new { w.Id, w.LaborCostPerHour, w.BurdenRatePerHour, w.BurdenBasis })
            .ToDictionaryAsync(w => w.Id, w => (w.LaborCostPerHour, w.BurdenRatePerHour, w.BurdenBasis), ct);

        decimal labor = 0m, overhead = 0m;
        foreach (var op in ops)
        {
            if (op.Ms <= 0 || !rates.TryGetValue(op.WorkCenterId, out var rate))
                continue;
            var hours = op.Ms / 3_600_000m;
            var laborHours = hours * op.LaborAttendancePercent / 100m;
            if (frozen is not null && frozen.ByWorkCenter.TryGetValue(op.WorkCenterId, out var period))
            {
                var burdenHours = rate.BurdenBasis == BurdenBasis.Machine ? hours : laborHours;
                labor += laborHours * period.LaborRate;
                overhead += laborHours * period.LaborOhRate
                    + burdenHours * period.MachineRate
                    + hours * (period.MachineOhVarRate + period.MachineOhFixedRate);
                continue;
            }

            var opLabor = laborHours * rate.LaborCostPerHour;
            labor += opLabor;

            // Tier 2 (departmental): a per-work-center percentage of this op's direct labor. Work centers
            // with no configured departmental rate — and Tier-1/flat mode — fall back to burden × hours.
            overhead += rate.BurdenBasis == BurdenBasis.Machine
                ? policy.Overhead(op.WorkCenterId, hours, hours * rate.LaborCostPerHour, rate.BurdenRatePerHour)
                : policy.Overhead(op.WorkCenterId, laborHours, opLabor, rate.BurdenRatePerHour);
        }
        return (labor, overhead);
    }
}
