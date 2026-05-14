
namespace Forge.Core.Models;

public record BinMovementResponseModel(
    int Id,
    string EntityType,
    int EntityId,
    string EntityName,
    decimal Quantity,
    string? LotNumber,
    int? FromLocationId,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationName,
    string MovedByName,
    DateTimeOffset MovedAt,
    BinMovementReason? Reason);
