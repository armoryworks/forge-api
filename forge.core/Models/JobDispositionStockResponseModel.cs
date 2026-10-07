namespace Forge.Core.Models;

public record JobDispositionStockResponseModel(
    int? PartId,
    bool HasSeveralParts,
    int? DefaultBinId,
    int ReceivedQuantity,
    int RecordedQuantity,
    bool HasOpenRuns);
