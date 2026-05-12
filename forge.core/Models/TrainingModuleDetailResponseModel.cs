using Forge.Core.Enums;

namespace Forge.Core.Models;

public record TrainingModuleDetailResponseModel(
    int Id,
    string Title,
    string Slug,
    string Summary,
    TrainingContentType ContentType,
    string? CoverImageUrl,
    int EstimatedMinutes,
    string[] Tags,
    bool IsPublished,
    bool IsOnboardingRequired,
    int SortOrder,
    TrainingProgressStatus? MyStatus,
    int? MyQuizScore,
    DateTimeOffset? MyCompletedAt,
    string ContentJson,
    string[] AppRoutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
