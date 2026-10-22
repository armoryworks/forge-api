using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Resolves a part's standard unit cost into material / labor / overhead. The total follows
/// <see cref="PartStandardCostRule"/>: a manual standard override is the cost of record whenever it is set.
/// <list type="number">
///   <item><b>Explicit cost-calc breakdown</b> — <c>CurrentCostCalculation.Inputs</c>
///         (DirectMaterialCost / DirectLaborCost / OverheadAmount), when any element is populated (the D5
///         persisted snapshot); under an override the breakdown is reconciled to the override.</item>
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

        if (PartStandardCostRule.CalculationElements(part) is { } fromCalculation)
            return fromCalculation;

        var elements = await rollup.RollupAsync(partId, ct);
        return PartStandardCostRule.ReconcileToCostOfRecord(part, elements);
    }
}
