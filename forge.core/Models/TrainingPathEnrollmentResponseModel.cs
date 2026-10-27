namespace Forge.Core.Models;

public record TrainingPathEnrollmentResponseModel(
    int Id,
    int UserId,
    string UserName,
    bool UserIsActive,
    DateTimeOffset EnrolledAt,
    DateOnly? DueDate,
    bool IsOverdue,
    bool IsAutoAssigned,
    string? AssignedByName,
    int TotalModules,
    int CompletedModules,
    double ProgressPercent,
    DateTimeOffset? CompletedAt);
