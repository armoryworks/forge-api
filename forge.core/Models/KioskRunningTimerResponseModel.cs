using Forge.Core.Enums;

namespace Forge.Core.Models;

public record KioskRunningTimerResponseModel(
    int TimeEntryId,
    int? JobId,
    string? JobNumber,
    int? JobOperationId,
    int? OperationId,
    int? StepNumber,
    string? OperationTitle,
    TimeEntryType EntryType,
    DateTimeOffset TimerStart,
    decimal AttendancePercent,
    decimal? CycleMinutes = null);
