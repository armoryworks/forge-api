namespace Forge.Core.Models;

/// <summary>UoM purchase-options effort — a part's purchasable size/form (read shape).</summary>
public record PartPurchaseOptionResponseModel(
    int Id,
    int PartId,
    string Label,
    decimal ContentQuantity,
    int? ContentUomId,
    string? ContentUomCode,
    string? ContentUomLabel,
    int SortOrder,
    bool IsActive);
