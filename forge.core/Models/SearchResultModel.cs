namespace Forge.Core.Models;

public record SearchResultModel(
    string EntityType,
    int EntityId,
    string Title,
    string? Subtitle,
    string Icon,
    string Url);
