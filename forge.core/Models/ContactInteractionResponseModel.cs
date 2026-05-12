namespace Forge.Core.Models;

public record ContactInteractionResponseModel(
    int Id,
    int ContactId,
    string ContactName,
    int UserId,
    string UserName,
    string Type,
    string Subject,
    string? Body,
    DateTimeOffset InteractionDate,
    int? DurationMinutes,
    DateTimeOffset CreatedAt);
