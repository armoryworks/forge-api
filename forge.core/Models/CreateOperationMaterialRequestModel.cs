namespace Forge.Core.Models;

public record CreateOperationMaterialRequestModel(
    int BomEntryId,
    decimal Quantity,
    string? Notes);
