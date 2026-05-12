namespace Forge.Core.Models;

public record UpdateTimeEntryRequestModel(
    int? JobId,
    DateOnly? Date,
    int? DurationMinutes,
    string? Category,
    string? Notes);
