namespace Forge.Core.Entities;

public class PredictionFeedback : BaseEntity
{
    public int PredictionId { get; set; }
    public bool ActualFailureOccurred { get; set; }
    public DateTimeOffset? ActualFailureDate { get; set; }
    public decimal? PredictionErrorHours { get; set; }
    public string? Notes { get; set; }
    public int? RecordedByUserId { get; set; }

    // Navigation (FK-only for ApplicationUser)
    public MaintenancePrediction Prediction { get; set; } = null!;
}
