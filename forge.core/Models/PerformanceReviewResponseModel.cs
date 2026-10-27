using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PerformanceReviewResponseModel(
    int Id,
    int CycleId,
    string CycleName,
    int EmployeeId,
    string EmployeeName,
    int ReviewerId,
    string ReviewerName,
    ReviewStatus Status,
    decimal? OverallRating,
    string? GoalsJson,
    string? CompetenciesJson,
    string? StrengthsComments,
    string? ImprovementComments,
    string? EmployeeSelfAssessment,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? AcknowledgedAt,
    string? SelfRatingsJson = null,
    DateTimeOffset? SelfSubmittedAt = null,
    DateTimeOffset? ManagerSubmittedAt = null,
    string? AcknowledgementComments = null,
    int RatingScaleMax = 5,
    IReadOnlyList<string>? CycleCompetencies = null);
