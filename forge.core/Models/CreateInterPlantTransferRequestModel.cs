namespace Forge.Core.Models;

public record CreateInterPlantTransferRequestModel(
    int FromPlantId,
    int ToPlantId,
    string? Notes,
    List<CreateInterPlantTransferLineRequestModel> Lines);

public record CreateInterPlantTransferLineRequestModel(
    int PartId,
    decimal Quantity,
    int? FromLocationId,
    int? ToLocationId,
    string? LotNumber);
