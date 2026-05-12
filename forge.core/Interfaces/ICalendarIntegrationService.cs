using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICalendarIntegrationService
{
    string ProviderId { get; }

    Task<string> PushEventAsync(int userId, int integrationId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task UpdateEventAsync(int userId, int integrationId, string externalEventId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(int userId, int integrationId, string externalEventId, CancellationToken ct = default);
    Task<List<CalendarFreeBusy>> GetFreeBusyAsync(int userId, int integrationId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    Task<CalendarSyncResult> SyncEventsAsync(int userId, int integrationId, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default);
}
