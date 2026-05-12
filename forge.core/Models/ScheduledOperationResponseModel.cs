using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ScheduledOperationResponseModel(
    int Id,
    int JobId,
    string JobNumber,
    string? JobTitle,
    int OperationId,
    string OperationTitle,
    int WorkCenterId,
    string WorkCenterName,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    decimal SetupHours,
    decimal RunHours,
    decimal TotalHours,
    ScheduledOperationStatus Status,
    int SequenceNumber,
    bool IsLocked,
    string? JobPriority,
    DateTimeOffset? JobDueDate,
    string? Color);
