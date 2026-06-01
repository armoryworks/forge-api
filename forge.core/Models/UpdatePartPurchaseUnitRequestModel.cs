namespace Forge.Core.Models;

/// <summary>UoM purchase-units effort — update a part's purchasable size/form (full replace).</summary>
public record UpdatePartPurchaseUnitRequestModel(
    string Label,
    decimal ContentQuantity,
    int? ContentUomId,
    int SortOrder,
    bool IsActive);
