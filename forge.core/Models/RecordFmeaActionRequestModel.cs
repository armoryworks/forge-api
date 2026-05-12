namespace Forge.Core.Models;

public record RecordFmeaActionRequestModel
{
    public string ActionTaken { get; init; } = string.Empty;
    public int? RevisedSeverity { get; init; }
    public int? RevisedOccurrence { get; init; }
    public int? RevisedDetection { get; init; }
}
