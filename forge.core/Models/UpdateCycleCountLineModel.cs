namespace Forge.Core.Models;

public record UpdateCycleCountLineModel(
    int Id,
    decimal ActualQuantity,
    string? Notes);
