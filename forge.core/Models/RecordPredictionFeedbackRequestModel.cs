namespace Forge.Core.Models;

public record RecordPredictionFeedbackRequestModel
{
    public bool ActualFailureOccurred { get; init; }
    public DateTimeOffset? ActualFailureDate { get; init; }
    public string? Notes { get; init; }
}
