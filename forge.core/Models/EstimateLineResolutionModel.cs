using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// #24: one per-line decision for the estimate → quote convert. Targets an
/// existing estimate line by id. PartId is required (and must reference an
/// active part) when <see cref="Action"/> is ReplaceWithPart; UnitPrice is
/// optional — when omitted on a replace, the customer's price-list price is
/// resolved, falling back to the line's existing amount.
/// </summary>
public record EstimateLineResolutionModel(
    int EstimateLineId,
    EstimateLineResolutionAction Action,
    int? PartId,
    decimal? UnitPrice);
