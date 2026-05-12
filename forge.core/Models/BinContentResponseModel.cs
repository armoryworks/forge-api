using Forge.Core.Enums;

namespace Forge.Core.Models;

public record BinContentResponseModel(
    int Id,
    int LocationId,
    string LocationName,
    string LocationPath,
    string EntityType,
    int EntityId,
    string EntityName,
    decimal Quantity,
    string? LotNumber,
    int? JobId,
    string? JobNumber,
    BinContentStatus Status,
    DateTimeOffset PlacedAt);
