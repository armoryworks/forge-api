namespace Forge.Core.Models;

public record StatusEntryResponseModel(
    int Id,
    string EntityType,
    int EntityId,
    string StatusCode,
    string StatusLabel,
    string Category,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Notes,
    int? SetById,
    string? SetByName,
    DateTimeOffset CreatedAt);
