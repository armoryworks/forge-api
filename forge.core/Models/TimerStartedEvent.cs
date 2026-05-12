namespace Forge.Core.Models;

public record TimerStartedEvent(
    int UserId,
    TimeEntryResponseModel Entry);
