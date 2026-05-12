namespace Forge.Core.Models;

public record BomExplosionStockItemModel(
    int PartId,
    string PartNumber,
    string Description,
    decimal Quantity,
    decimal ReservedQuantity,
    bool HasShortfall);
