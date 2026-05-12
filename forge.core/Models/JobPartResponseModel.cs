namespace Forge.Core.Models;

public record JobPartResponseModel(
    int Id,
    int JobId,
    int PartId,
    string PartNumber,
    string PartDescription,
    string PartStatus,
    decimal Quantity,
    string? Notes);
