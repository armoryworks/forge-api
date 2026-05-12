using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MaintenancePrediction : BaseEntity
{
    public int WorkCenterId { get; set; }
    public string PredictionType { get; set; } = string.Empty;
    public decimal ConfidencePercent { get; set; }
    public DateTimeOffset PredictedFailureDate { get; set; }
    public decimal? RemainingUsefulLifeHours { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string InputFeaturesJson { get; set; } = "{}";
    public MaintenancePredictionStatus Status { get; set; } = MaintenancePredictionStatus.Predicted;
    public MaintenancePredictionSeverity Severity { get; set; }
    public DateTimeOffset PredictedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public int? AcknowledgedByUserId { get; set; }
    public int? PreventiveMaintenanceJobId { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool WasAccurate { get; set; }

    // Navigation (FK-only for ApplicationUser)
    public WorkCenter WorkCenter { get; set; } = null!;
    public Job? PreventiveMaintenanceJob { get; set; }
}
