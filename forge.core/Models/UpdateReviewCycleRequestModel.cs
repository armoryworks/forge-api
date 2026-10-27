namespace Forge.Core.Models;

public record UpdateReviewCycleRequestModel(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    int RatingScaleMax,
    IReadOnlyList<string>? Competencies,
    string? ScopeType,
    string? ScopeDepartment,
    IReadOnlyList<int>? ScopeUserIds);
