using Forge.Core.Entities;

namespace Forge.Core.Models;

/// <summary>
/// The standard unit cost the books carry for a part: <c>ManualCostOverride</c> when set, otherwise
/// the current cost calculation's result (the same precedence as the standard-cost resolver).
/// <see cref="Source"/> says which one it is; the calculation's amount, date and element breakdown
/// ride along whenever a current calculation exists, even under an override.
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
        var calc = part.CurrentCostCalculation;
        if (part.ManualCostOverride is null && calc is null)
            return null;

        return new PartStandardCostModel(
            part.ManualCostOverride ?? calc!.ResultAmount,
            part.ManualCostOverride.HasValue ? OverrideSource : CalculationSource,
            calc?.ResultAmount,
            calc?.CalculatedAt,
            calc?.Inputs?.DirectMaterialCost,
            calc?.Inputs?.DirectLaborCost,
            calc?.Inputs?.OverheadAmount);
    }
}
