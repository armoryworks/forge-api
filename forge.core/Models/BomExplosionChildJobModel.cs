namespace Forge.Core.Models;

public record BomExplosionChildJobModel(
    int JobId,
    string JobNumber,
    string Title,
    int PartId,
    string PartNumber,
    decimal Quantity);
