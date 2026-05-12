namespace Forge.Core.Models;

public record DispatchListItemModel(
    int ScheduledOperationId,
    int JobId,
    string JobNumber,
    int OperationId,
    string OperationTitle,
    int SequenceNumber,
    DateTimeOffset ScheduledStart,
    decimal SetupHours,
    decimal RunHours,
    string? Priority,
    DateTimeOffset? JobDueDate);
