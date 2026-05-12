namespace Forge.Core.Models;

public record CreateProductionRunRequestModel(
    int PartId,
    int TargetQuantity,
    int? OperatorId,
    string? Notes);
