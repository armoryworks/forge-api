namespace Forge.Core.Models;

public record BoardJobPositionChangedEvent(
    int JobId,
    int StageId,
    int NewPosition);
