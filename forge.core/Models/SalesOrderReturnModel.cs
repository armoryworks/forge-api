namespace Forge.Core.Models;

public record SalesOrderReturnModel(
    int Id,
    string ReturnNumber,
    string Status,
    string? Reason,
    DateTimeOffset? ReturnDate,
    int? OriginalJobId,
    string? OriginalJobNumber,
    int? ReworkJobId,
    string? ReworkJobNumber,
    string? InspectionNotes);
