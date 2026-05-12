namespace Forge.Core.Models;

public record RdReportItem(
    int JobId,
    string JobNumber,
    string Title,
    int IterationCount,
    decimal TotalHours,
    string CurrentStage,
    string? AssigneeName,
    DateTimeOffset? StartDate,
    DateTimeOffset? CompletedDate);
