namespace Forge.Core.Models;

public record CreateReviewCycleRequestModel(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    int? RatingScaleMax = null,
    IReadOnlyList<string>? Competencies = null,
    string? ScopeType = null,
    string? ScopeDepartment = null,
    IReadOnlyList<int>? ScopeUserIds = null);
