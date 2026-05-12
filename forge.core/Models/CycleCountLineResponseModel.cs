namespace Forge.Core.Models;

public record CycleCountLineResponseModel(
    int Id,
    int? BinContentId,
    string EntityType,
    int EntityId,
    string EntityName,
    int ExpectedQuantity,
    int ActualQuantity,
    int Variance,
    string? Notes);
