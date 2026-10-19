namespace Forge.Core.Models;

public record JobIssuableMaterialResponseModel(
    int PartId,
    string PartNumber,
    string? Description,
    decimal PerUnit,
    decimal RequiredQuantity,
    decimal IssuedQuantity,
    int? AlternateForPartId,
    string? AlternateForPartNumber);
