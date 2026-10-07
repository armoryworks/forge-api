namespace Forge.Api.Features.TimeTracking;

public record StoppedTimerResponseModel(int TimeEntryId, int? JobId, string? JobNumber);
