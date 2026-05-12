namespace Forge.Core.Models;

public record PredictiveMaintenanceDashboardResponseModel
{
    public int ActivePredictions { get; init; }
    public int CriticalPredictions { get; init; }
    public int PendingAcknowledgment { get; init; }
    public int MaintenanceScheduled { get; init; }
    public decimal OverallModelAccuracy { get; init; }
    public decimal EstimatedDowntimePreventedHours { get; init; }
    public IReadOnlyList<WorkCenterRiskScoreResponseModel> WorkCenterRisks { get; init; } = [];
    public IReadOnlyList<UpcomingPredictionResponseModel> UpcomingPredictions { get; init; } = [];
}

public record WorkCenterRiskScoreResponseModel
{
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public decimal RiskScore { get; init; }
    public string HighestSeverityPrediction { get; init; } = string.Empty;
    public DateTimeOffset? NextPredictedFailure { get; init; }
}

public record UpcomingPredictionResponseModel
{
    public int Id { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public string PredictionType { get; init; } = string.Empty;
    public decimal ConfidencePercent { get; init; }
    public DateTimeOffset PredictedFailureDate { get; init; }
    public string Severity { get; init; } = string.Empty;
}
