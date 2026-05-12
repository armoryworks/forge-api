namespace Forge.Core.Models;

public record CustomerJobSummaryModel(
    int Id,
    string JobNumber,
    string Title,
    string? StageName,
    string? StageColor,
    DateTimeOffset? DueDate);
