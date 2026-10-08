namespace Forge.Core.Models;

public record MyJobResponseModel(
    int Id,
    string JobNumber,
    string Title,
    string? PartNumber,
    decimal? Quantity,
    DateTimeOffset? DueDate,
    int StageId,
    string StageName,
    bool IsOverdue,
    bool HasRunningTimer);
