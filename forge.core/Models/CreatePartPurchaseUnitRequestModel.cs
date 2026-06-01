namespace Forge.Core.Models;

/// <summary>UoM purchase-units effort — create a purchasable size/form for a part.</summary>
public record CreatePartPurchaseUnitRequestModel(
    string Label,
    decimal ContentQuantity,
    int? ContentUomId,
    int SortOrder);
