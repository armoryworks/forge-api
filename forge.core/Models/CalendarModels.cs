namespace Forge.Core.Models;

public record CalendarEvent(
    string? ExternalId,
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    bool IsAllDay = false,
    string? RecurrenceRule = null);

public record CalendarFreeBusy(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsBusy);

public record CalendarSyncResult(
    int Created,
    int Updated,
    int Deleted,
    string? SyncToken);
