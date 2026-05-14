
namespace Forge.Core.Models;

public record StartTimerRequestModel(
    int? JobId,
    string? Category,
    string? Notes,
    int? OperationId = null,
    TimeEntryType EntryType = TimeEntryType.Run);
