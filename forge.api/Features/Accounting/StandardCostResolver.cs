using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Resolves a part's standard unit cost into material / labor / overhead. Resolution priority:
/// <list type="number">
///   <item><b>Explicit cost-calc breakdown</b> — <c>CurrentCostCalculation.Inputs</c>
///         (DirectMaterialCost / DirectLaborCost / OverheadAmount), when any element is populated (the D5
///         persisted snapshot).</item>
///   <item><b>Manual standard override present</b> — <c>ManualCostOverride</c> is the carried standard
///         <i>total</i>; labor + overhead come from the live cost rollup (routing × work-center rates) and
///         material is the reconciling residual (override − labor − overhead). This keeps the element total
///         equal to the carried standard so the variance decomposition balances exactly.</item>
///   <item><b>No override</b> — the cost rollup IS the standard (material from BOM + labor/overhead from
///         routing); the elements sum to the rolled-up total.</item>
/// </list>
/// </summary>
public sealed class StandardCostResolver(AppDbContext db, IStandardCostRollupService rollup) : IStandardCostResolver
{
    public async Task<StandardCostElements> ResolveAsync(int partId, CancellationToken ct = default)
    {
        var part = await db.Parts.AsNoTracking()
            .Include(p => p.CurrentCostCalculation).ThenInclude(c => c!.Inputs)
            .FirstOrDefaultAsync(p => p.Id == partId, ct);
        if (part is null)
            return StandardCostElements.Zero;

        // 1) Explicit element breakdown from the current cost calculation (D5 snapshot).
        var inputs = part.CurrentCostCalculation?.Inputs;
        if (inputs is not null &&
            (inputs.DirectMaterialCost.HasValue || inputs.DirectLaborCost.HasValue || inputs.OverheadAmount.HasValue))
        {
            return new StandardCostElements(
                inputs.DirectMaterialCost ?? 0m,
                inputs.DirectLaborCost ?? 0m,
                inputs.OverheadAmount ?? 0m);
        }

        // 2) Manual override → it is the carried total (labor/overhead from rollup, material = residual);
        //    3) no override → the rollup is the standard. (Shared reconcile with the part-standard recalc.)
        var elements = await rollup.RollupAsync(partId, ct);
        var manualOverride = part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount;
        return StandardCostElements.ReconcileToOverride(elements, manualOverride);
    }
}
