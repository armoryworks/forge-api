namespace Forge.Core.Models;

public record AdminCorrectTimeEntryRequestModel(
    int? JobId,
    DateOnly? Date,
    int? DurationMinutes,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? Category,
    string? Notes,
    string Reason);
