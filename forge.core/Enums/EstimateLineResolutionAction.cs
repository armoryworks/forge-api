namespace Forge.Core.Enums;

/// <summary>
/// #24: per-line decision applied while converting an estimate to a quote.
/// Lump-sum estimate lines (PartId null) are resolved at convert time by
/// either dropping them or attaching a real catalog part.
/// </summary>
public enum EstimateLineResolutionAction
{
    /// <summary>Carry the line into the quote unchanged (default when no resolution is supplied).</summary>
    Keep,

    /// <summary>Drop the line — it does not appear on the generated quote.</summary>
    Eliminate,

    /// <summary>Attach a real catalog part to the line (PartId required; UnitPrice optional — resolved from the customer's price list when omitted).</summary>
    ReplaceWithPart,
}
