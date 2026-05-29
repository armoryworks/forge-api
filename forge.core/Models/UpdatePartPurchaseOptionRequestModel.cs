namespace Forge.Core.Models;

/// <summary>UoM purchase-options effort — update a part's purchasable size/form (full replace).</summary>
public record UpdatePartPurchaseOptionRequestModel(
    string Label,
    decimal ContentQuantity,
    int? ContentUomId,
    int SortOrder,
    bool IsActive);
