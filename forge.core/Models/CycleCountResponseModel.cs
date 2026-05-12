namespace Forge.Core.Models;

public record CycleCountResponseModel(
    int Id,
    int LocationId,
    string LocationName,
    int CountedById,
    string CountedByName,
    DateTimeOffset CountedAt,
    string Status,
    string? Notes,
    List<CycleCountLineResponseModel> Lines,
    DateTimeOffset CreatedAt);
