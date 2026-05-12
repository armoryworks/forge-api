namespace Forge.Core.Models;

public record ContactInteractionRequestModel(
    int? ContactId,
    string Type,
    string Subject,
    string? Body,
    DateTimeOffset InteractionDate,
    int? DurationMinutes);
