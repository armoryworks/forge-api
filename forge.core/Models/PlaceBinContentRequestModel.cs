using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PlaceBinContentRequestModel(
    int LocationId,
    string EntityType,
    int EntityId,
    decimal Quantity,
    string? LotNumber,
    int? JobId,
    BinContentStatus Status,
    string? Notes);
