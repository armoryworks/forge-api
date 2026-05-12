namespace Forge.Core.Models;

public record BoardJobCreatedEvent(
    int JobId,
    string JobNumber,
    string Title,
    int TrackTypeId,
    int StageId,
    string StageName,
    int BoardPosition);
