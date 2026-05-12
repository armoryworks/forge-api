namespace Forge.Core.Models;

public record OverdueJobReportItem(
    int Id,
    string JobNumber,
    string Title,
    DateTimeOffset DueDate,
    int DaysOverdue,
    string? AssigneeName);
