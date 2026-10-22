using Forge.Core.Entities;
using Forge.Core.Models.Accounting;

namespace Forge.Core.Models;

/// <summary>
/// The single precedence rule for a part's standard unit cost, shared by the part detail and the posting
/// resolver: an explicit <c>ManualCostOverride</c> is the cost of record whenever it is set; otherwise the
/// current cost calculation's result is.
/// </summary>
public static class PartStandardCostRule
{
    /// <summary>True when the part's manual override is the cost of record.</summary>
    public static bool OverrideIsCostOfRecord(Part part) => part.ManualCostOverride.HasValue;

    /// <summary>
    /// The carried standard unit cost: the manual override when set, otherwise the current calculation's
    /// result; null when the part has neither.
    /// </summary>
    public static decimal? CostOfRecord(Part part) =>
        part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount;

    /// <summary>
    /// The current calculation's element breakdown (<c>Inputs</c>), or null when no element is populated.
    /// Under an override the breakdown is reconciled to the override so the elements sum to the cost of
    /// record; without one it is returned as calculated.
    /// </summary>
    public static StandardCostElements? CalculationElements(Part part)
    {
        var inputs = part.CurrentCostCalculation?.Inputs;
        if (inputs is null ||
            !(inputs.DirectMaterialCost.HasValue || inputs.DirectLaborCost.HasValue || inputs.OverheadAmount.HasValue))
            return null;

        var elements = new StandardCostElements(
            inputs.DirectMaterialCost ?? 0m,
            inputs.DirectLaborCost ?? 0m,
            inputs.OverheadAmount ?? 0m);
        return OverrideIsCostOfRecord(part) ? ReconcileToCostOfRecord(part, elements) : elements;
    }

    /// <summary>
    /// Reconciles a breakdown to the part's cost of record (see
    /// <see cref="StandardCostElements.ReconcileToOverride"/>); a zero override is a zero standard.
    /// </summary>
    public static StandardCostElements ReconcileToCostOfRecord(Part part, StandardCostElements elements) =>
        part.ManualCostOverride == 0m
            ? StandardCostElements.Zero
            : StandardCostElements.ReconcileToOverride(elements, CostOfRecord(part));
}
