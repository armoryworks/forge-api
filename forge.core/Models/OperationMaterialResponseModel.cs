namespace Forge.Core.Models;

public record OperationMaterialResponseModel(
    int Id,
    int OperationId,
    int BomLineId,
    string ChildPartNumber,
    string ChildPartName,
    decimal Quantity,
    string? Notes);
