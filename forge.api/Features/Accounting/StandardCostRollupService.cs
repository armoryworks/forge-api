using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
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

    public async Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default)
    {
        var policy = await CostingConversionPolicy.LoadAsync(
            db, capabilities, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct);
        return await RollupAsync(partId, policy, new HashSet<int>(), 0, ct);
    }

    private async Task<StandardCostElements> RollupAsync(int partId, CostingConversionPolicy policy, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth || !visited.Add(partId))
            return StandardCostElements.Zero; // cycle / runaway guard

        try
        {
            var (labor, overhead) = await RollupConversionAsync(partId, policy, ct);

            // Material from the live part BOM (BOMLine: parent → child × qty), recursive.
            var bomLines = await db.BOMLines.AsNoTracking()
                .Where(b => b.ParentPartId == partId)
                .Select(b => new { b.ChildPartId, b.Quantity })
                .ToListAsync(ct);

            decimal material = 0m;
            foreach (var line in bomLines)
            {
                var child = await RollupAsync(line.ChildPartId, policy, visited, depth + 1, ct);
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
    private async Task<(decimal Labor, decimal Overhead)> RollupConversionAsync(int partId, CostingConversionPolicy policy, CancellationToken ct)
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
