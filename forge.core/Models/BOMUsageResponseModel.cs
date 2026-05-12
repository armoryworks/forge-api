namespace Forge.Core.Models;

public record BOMUsageResponseModel(
    int Id,
    int ParentPartId,
    string ParentPartNumber,
    string ParentName,
    decimal Quantity);
