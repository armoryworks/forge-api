namespace Forge.Core.Models;

public record TrainingEnrollmentResponseModel(
    int Id,
    int PathId,
    string PathTitle,
    string PathIcon,
    int TotalModules,
    int CompletedModules,
    DateTimeOffset? EnrolledAt,
    DateTimeOffset? CompletedAt
);
