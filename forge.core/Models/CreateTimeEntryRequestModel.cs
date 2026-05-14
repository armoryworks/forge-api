
namespace Forge.Core.Models;

public record CreateTimeEntryRequestModel(
    int? JobId,
    DateOnly Date,
    int DurationMinutes,
    string? Category,
    string? Notes,
    int? OperationId = null,
    TimeEntryType EntryType = TimeEntryType.Run);
