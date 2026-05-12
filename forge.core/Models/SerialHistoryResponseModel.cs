namespace Forge.Core.Models;

public record SerialHistoryResponseModel(
    int Id,
    int SerialNumberId,
    string Action,
    string? FromLocationName,
    string? ToLocationName,
    int? ActorId,
    string? Details,
    DateTimeOffset OccurredAt);
