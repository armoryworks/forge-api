namespace Forge.Core.Models;

public record ActiveTimerResponseModel(
    int TimeEntryId,
    int? JobId,
    string? JobNumber,
    int? OperationId,
    DateTimeOffset TimerStart);
