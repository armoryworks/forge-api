namespace Forge.Core.Models;

public record RagSearchResultModel(
    string EntityType,
    int EntityId,
    string ChunkText,
    string? SourceField,
    double Score);
