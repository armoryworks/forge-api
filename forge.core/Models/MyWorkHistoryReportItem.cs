namespace Forge.Core.Models;

public record MyWorkHistoryReportItem(
    int JobId,
    string JobNumber,
    string Title,
    string StageName,
    string? StageColor,
    string? CustomerName,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);
