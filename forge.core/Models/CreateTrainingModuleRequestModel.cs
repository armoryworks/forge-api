
namespace Forge.Core.Models;

public record CreateTrainingModuleRequestModel(
    string Title,
    string Slug,
    string Summary,
    TrainingContentType ContentType,
    string ContentJson,
    string? CoverImageUrl,
    int EstimatedMinutes,
    string[] Tags,
    string[] AppRoutes,
    bool IsPublished,
    bool IsOnboardingRequired,
    int SortOrder
);
