using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ReviewCycleResponseModel(
    int Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    ReviewCycleStatus Status,
    string? Description,
    int ReviewCount,
    int RatingScaleMax = 5,
    IReadOnlyList<string>? Competencies = null,
    string? ScopeType = null,
    string? ScopeDepartment = null,
    IReadOnlyList<int>? ScopeUserIds = null,
    DateTimeOffset? LaunchedAt = null,
    DateTimeOffset? ClosedAt = null,
    ReviewCycleProgressModel? Progress = null);
