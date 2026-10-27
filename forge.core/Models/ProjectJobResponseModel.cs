namespace Forge.Core.Models;

/// <summary>A job linked to one of the project's WBS elements.</summary>
public record ProjectJobResponseModel(
    int JobId,
    string JobNumber,
    string Title,
    string? StageName,
    string? StageColor,
    DateTimeOffset? DueDate,
    DateTimeOffset? CompletedDate,
    int WbsElementId,
    string WbsCode,
    string WbsName);
