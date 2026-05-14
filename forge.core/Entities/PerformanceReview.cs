
namespace Forge.Core.Entities;

public class PerformanceReview : BaseAuditableEntity
{
    public int CycleId { get; set; }
    public int EmployeeId { get; set; }
    public int ReviewerId { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.NotStarted;
    public decimal? OverallRating { get; set; }
    public string? GoalsJson { get; set; }
    public string? CompetenciesJson { get; set; }
    public string? StrengthsComments { get; set; }
    public string? ImprovementComments { get; set; }
    public string? EmployeeSelfAssessment { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    public ReviewCycle Cycle { get; set; } = null!;
}
