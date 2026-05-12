namespace Forge.Core.Models;

public record InventoryLevelReportItem(
    int PartId,
    string PartNumber,
    string Description,
    decimal CurrentStock,
    decimal? MinStockThreshold,
    decimal? ReorderPoint,
    bool IsLowStock);
