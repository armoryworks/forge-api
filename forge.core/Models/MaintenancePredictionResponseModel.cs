
namespace Forge.Core.Models;

public record MaintenancePredictionResponseModel
{
    public int Id { get; init; }
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public string PredictionType { get; init; } = string.Empty;
    public decimal ConfidencePercent { get; init; }
    public DateTimeOffset PredictedFailureDate { get; init; }
    public decimal? RemainingUsefulLifeHours { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public MaintenancePredictionSeverity Severity { get; init; }
    public MaintenancePredictionStatus Status { get; init; }
    public DateTimeOffset PredictedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public string? AcknowledgedByName { get; init; }
    public int? PreventiveMaintenanceJobId { get; init; }
    public string? ResolutionNotes { get; init; }
    public bool WasAccurate { get; init; }
    public string InputFeaturesJson { get; init; } = "{}";
}
