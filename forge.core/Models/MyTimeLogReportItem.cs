namespace Forge.Core.Models;

public record MyTimeLogReportItem(
    int TimeEntryId,
    string? JobNumber,
    string? JobTitle,
    string? Notes,
    int DurationMinutes,
    string? Category,
    DateOnly Date
);
