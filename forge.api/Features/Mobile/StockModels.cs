namespace Forge.Api.Features.Mobile;

public record OnHandLotModel(string LotNumber, decimal Quantity);

public record OnHandResponseModel(
    int PartId,
    string PartNumber,
    int LocationId,
    string LocationName,
    decimal Quantity,
    bool LotTracked,
    List<OnHandLotModel> Lots);

/// <summary>The reverse move the undo toast issues.</summary>
public record StockMoveUndoModel(
    int PartId,
    int FromLocationId,
    int ToLocationId,
    decimal Quantity,
    string? LotNumber);

public record StockMoveResponseModel(
    string PartNumber,
    string FromLocationName,
    string ToLocationName,
    decimal Quantity,
    string? LotNumber,
    StockMoveUndoModel Undo);
