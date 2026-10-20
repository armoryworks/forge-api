namespace Forge.Core.Models;

public record CycleCountLineResponseModel(
    int Id,
    int? BinContentId,
    string EntityType,
    int EntityId,
    string EntityName,
    decimal ExpectedQuantity,
    decimal ActualQuantity,
    decimal Variance,
    string? Notes);
