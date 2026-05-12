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
    DateTimeOffset? AcknowledgedAt);
