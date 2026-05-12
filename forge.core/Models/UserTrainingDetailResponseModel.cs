namespace Forge.Core.Models;

public record UserTrainingModuleDetail(
    int ModuleId,
    string Title,
    string ContentType,
    string? Status,
    double? QuizScore,
    int QuizAttempts,
    int TimeSpentSeconds,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);

public record UserTrainingDetailResponseModel(
    int UserId,
    string DisplayName,
    string Role,
    int TotalEnrolled,
    double OverallCompletionPct,
    IReadOnlyList<UserTrainingModuleDetail> Modules
);
