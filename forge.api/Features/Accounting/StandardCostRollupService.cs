using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Computes a part's standard unit cost decomposed into material / labor / overhead from the captured master
/// data — the "cost rollup" the variance decomposition needs (the D5 cost-recalc engine, used live by
/// <see cref="StandardCostResolver"/> now and available to persist into <c>CostCalculation</c> later):
/// <list type="bullet">
///   <item><b>Labor</b> = Σ operations <c>(EstimatedMinutes/60) × WorkCenter.LaborCostPerHour</c>.</item>
///   <item><b>Overhead</b> = Σ operations <c>(EstimatedMinutes/60) × WorkCenter.BurdenRatePerHour</c>
///         (work-center burden rate × hours).</item>
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
public sealed class StandardCostRollupService(AppDbContext db) : IStandardCostRollupService
{
    private const int MaxDepth = 32; // defensive bound on BOM depth (cycle guard already prevents true loops)

    public Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default)
        => RollupAsync(partId, new HashSet<int>(), 0, ct);

    private async Task<StandardCostElements> RollupAsync(int partId, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth || !visited.Add(partId))
            return StandardCostElements.Zero; // cycle / runaway guard

        try
        {
            var (labor, overhead) = await RollupConversionAsync(partId, ct);

            // Material from the live part BOM (BOMLine: parent → child × qty), recursive.
            var bomLines = await db.BOMLines.AsNoTracking()
                .Where(b => b.ParentPartId == partId)
                .Select(b => new { b.ChildPartId, b.Quantity })
                .ToListAsync(ct);

            decimal material = 0m;
            foreach (var line in bomLines)
            {
                var child = await RollupAsync(line.ChildPartId, visited, depth + 1, ct);
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
    private async Task<(decimal Labor, decimal Overhead)> RollupConversionAsync(int partId, CancellationToken ct)
    {
        var ops = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId && !o.IsSubcontract && o.WorkCenterId != null && o.EstimatedMinutes != null)
            .Select(o => new { Minutes = o.EstimatedMinutes!.Value, WorkCenterId = o.WorkCenterId!.Value })
            .ToListAsync(ct);
        if (ops.Count == 0)
            return (0m, 0m);

        var wcIds = ops.Select(o => o.WorkCenterId).Distinct().ToList();
        var rates = await db.WorkCenters.AsNoTracking()
            .Where(w => wcIds.Contains(w.Id))
            .Select(w => new { w.Id, w.LaborCostPerHour, w.BurdenRatePerHour })
            .ToDictionaryAsync(w => w.Id, w => (w.LaborCostPerHour, w.BurdenRatePerHour), ct);

        decimal labor = 0m, overhead = 0m;
        foreach (var op in ops)
        {
            if (op.Minutes <= 0 || !rates.TryGetValue(op.WorkCenterId, out var rate))
                continue;
            var hours = op.Minutes / 60m;
            labor += hours * rate.LaborCostPerHour;
            overhead += hours * rate.BurdenRatePerHour;
        }
        return (labor, overhead);
    }
}
