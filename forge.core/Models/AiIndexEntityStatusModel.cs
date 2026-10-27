namespace Forge.Core.Models;

public record AiIndexEntityStatusModel(
    string EntityType,
    int IndexedCount,
    int? TotalCount,
    int ChunkCount,
    DateTimeOffset? LastIndexedAt);
