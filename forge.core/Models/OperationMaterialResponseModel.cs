namespace Forge.Core.Models;

public record OperationMaterialResponseModel(
    int Id,
    int OperationId,
    int BomEntryId,
    string ChildPartNumber,
    string ChildPartName,
    decimal Quantity,
    string? Notes);
