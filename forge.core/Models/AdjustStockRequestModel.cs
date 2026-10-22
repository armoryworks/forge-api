namespace Forge.Core.Models;

public record AdjustStockRequestModel(
    int BinContentId,
    decimal NewQuantity,
    string Reason,
    string? Notes);
