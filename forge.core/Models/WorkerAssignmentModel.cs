namespace Forge.Core.Models;

public record WorkerAssignmentModel(
    int JobId,
    string JobNumber,
    string Title,
    string PriorityName,
    string StageName,
    string StageColor,
    bool IsOverdue,
    bool HasActiveTimer);
