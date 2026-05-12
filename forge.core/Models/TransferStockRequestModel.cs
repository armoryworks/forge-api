namespace Forge.Core.Models;

public record TransferStockRequestModel(
    int SourceBinContentId,
    int DestinationLocationId,
    int Quantity,
    string? Notes);
