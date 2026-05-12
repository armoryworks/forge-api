namespace Forge.Core.Models;

public record TimerStoppedEvent(
    int UserId,
    TimeEntryResponseModel Entry);
