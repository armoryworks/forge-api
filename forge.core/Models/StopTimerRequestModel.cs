namespace Forge.Core.Models;

public record StopTimerRequestModel(
    string? Notes,
    int? TimeEntryId = null,
    int? JobId = null);
