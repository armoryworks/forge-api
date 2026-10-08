namespace Forge.Core.Models;

public record JobOperationOpenTimerResponseModel(
    int TimeEntryId,
    int UserId,
    string UserName,
    string? UserInitials,
    string EntryType,
    DateTimeOffset TimerStart);
