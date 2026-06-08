using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Resolves a part's standard unit cost into material / labor / overhead. Resolution priority:
/// <list type="number">
///   <item><b>Explicit cost-calc breakdown</b> — <c>CurrentCostCalculation.Inputs</c>
///         (DirectMaterialCost / DirectLaborCost / OverheadAmount), when any element is populated.</item>
///   <item><b>Routing rollup</b> — labor + overhead from the part's <see cref="Forge.Core.Entities.Operation"/>
///         standards (Σ EstimatedLaborCost / EstimatedBurdenCost); material is the residual of the blended
///         standard (<c>ManualCostOverride ?? CostCalc.ResultAmount</c>) minus labor + overhead, so the three
///         elements always reconcile to the standard value carried in inventory.</item>
///   <item><b>Blended fallback</b> — no routing and only a blended <c>ManualCostOverride</c>: all material
///         (no element split available).</item>
/// </list>
/// </summary>
public sealed class StandardCostResolver(AppDbContext db) : IStandardCostResolver
{
    public async Task<StandardCostElements> ResolveAsync(int partId, CancellationToken ct = default)
    {
        var part = await db.Parts.AsNoTracking()
            .Include(p => p.CurrentCostCalculation).ThenInclude(c => c!.Inputs)
            .FirstOrDefaultAsync(p => p.Id == partId, ct);
        if (part is null)
            return StandardCostElements.Zero;

        // 1) Explicit element breakdown from the current cost calculation.
        var inputs = part.CurrentCostCalculation?.Inputs;
        if (inputs is not null &&
            (inputs.DirectMaterialCost.HasValue || inputs.DirectLaborCost.HasValue || inputs.OverheadAmount.HasValue))
        {
            return new StandardCostElements(
                inputs.DirectMaterialCost ?? 0m,
                inputs.DirectLaborCost ?? 0m,
                inputs.OverheadAmount ?? 0m);
        }

        var blendedTotal = part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount ?? 0m;

        // 2) Routing rollup for labor + overhead; material is the reconciling residual.
        var routing = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId)
            .Select(o => new { o.EstimatedLaborCost, o.EstimatedBurdenCost })
            .ToListAsync(ct);

        if (routing.Count > 0)
        {
            var labor = routing.Sum(o => o.EstimatedLaborCost);
            var overhead = routing.Sum(o => o.EstimatedBurdenCost);
            var material = blendedTotal - labor - overhead;
            if (material < 0m) material = 0m; // a blended total below routing conversion → no implied material
            return new StandardCostElements(material, labor, overhead);
        }

        // 3) Blended-only fallback: no element split available.
        return new StandardCostElements(blendedTotal, 0m, 0m);
    }
}
