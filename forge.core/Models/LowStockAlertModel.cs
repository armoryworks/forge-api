namespace Forge.Core.Models;

public record LowStockAlertModel(
    int PartId,
    string PartNumber,
    string Description,
    decimal CurrentStock,
    decimal MinStockThreshold,
    decimal? ReorderPoint);
