using Forge.Core.Entities;

namespace Forge.Core.Models;

/// <summary>
/// The standard unit cost the books carry for a part, by <see cref="PartStandardCostRule"/>: an explicit
/// <c>ManualCostOverride</c> is the cost of record when set, otherwise the current cost calculation's result.
/// <see cref="Source"/> says which one it is. The calculation's amount and date ride along whenever a current
/// calculation exists, even under an override; the element breakdown is the one postings use, reconciled to
/// the override when one is set.
/// </summary>
public record PartStandardCostModel(
    decimal Amount,
    string Source,
    decimal? CalculatedAmount,
    DateTimeOffset? CalculatedAt,
    decimal? MaterialCost,
    decimal? LaborCost,
    decimal? OverheadCost)
{
    /// <summary>Source value when the manual override is the standard.</summary>
    public const string OverrideSource = "Override";

    /// <summary>Source value when the current cost calculation is the standard.</summary>
    public const string CalculationSource = "Calculation";

    /// <summary>
    /// Builds the model from a part loaded with <c>CurrentCostCalculation</c> (and its inputs);
    /// null when the part has neither an override nor a calculation.
    /// </summary>
    public static PartStandardCostModel? From(Part part)
    {
        if (PartStandardCostRule.CostOfRecord(part) is not decimal amount)
            return null;

        var calc = part.CurrentCostCalculation;
        var overrideWins = PartStandardCostRule.OverrideIsCostOfRecord(part);
        var elements = PartStandardCostRule.CalculationElements(part);
        return new PartStandardCostModel(
            amount,
            overrideWins ? OverrideSource : CalculationSource,
            calc?.ResultAmount,
            calc?.CalculatedAt,
            overrideWins ? elements?.Material : calc?.Inputs?.DirectMaterialCost,
            overrideWins ? elements?.Labor : calc?.Inputs?.DirectLaborCost,
            overrideWins ? elements?.Overhead : calc?.Inputs?.OverheadAmount);
    }
}
