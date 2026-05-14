
namespace Forge.Core.Models;

public record InterPlantTransferResponseModel(
    int Id,
    string TransferNumber,
    int FromPlantId,
    string FromPlantName,
    int ToPlantId,
    string ToPlantName,
    InterPlantTransferStatus Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    string? TrackingNumber,
    string? Notes,
    int LineCount,
    List<InterPlantTransferLineResponseModel> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record InterPlantTransferLineResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string PartDescription,
    decimal Quantity,
    decimal? ReceivedQuantity,
    string? LotNumber);
