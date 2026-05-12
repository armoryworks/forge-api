namespace Forge.Core.Models;

public record BoardJobMovedEvent(
    int JobId,
    int FromStageId,
    int ToStageId,
    string ToStageName,
    int BoardPosition);
