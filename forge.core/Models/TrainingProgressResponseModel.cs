using Forge.Core.Enums;

namespace Forge.Core.Models;

public record TrainingProgressResponseModel(
    int ModuleId,
    TrainingProgressStatus Status,
    int? QuizScore,
    int? QuizAttempts,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TimeSpentSeconds
);
