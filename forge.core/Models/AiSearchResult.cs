namespace Forge.Core.Models;

public record AiSearchResult(
    string EntityType,
    int EntityId,
    string Title,
    string Snippet,
    double Score);
