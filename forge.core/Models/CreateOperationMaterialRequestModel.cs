namespace Forge.Core.Models;

public record CreateOperationMaterialRequestModel(
    int BomLineId,
    decimal Quantity,
    string? Notes);
