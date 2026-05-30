namespace Forge.Core.Models;

/// <summary>UoM purchase-options effort — create a purchasable size/form for a part.</summary>
public record CreatePartPurchaseOptionRequestModel(
    string Label,
    decimal ContentQuantity,
    int? ContentUomId,
    int SortOrder);
