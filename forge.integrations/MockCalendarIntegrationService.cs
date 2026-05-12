using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockCalendarIntegrationService(ILogger<MockCalendarIntegrationService> logger) : ICalendarIntegrationService
{
    public string ProviderId => "mock_calendar";

    public Task<string> PushEventAsync(int userId, int integrationId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        var externalId = $"mock-cal-{Guid.NewGuid():N}";
        logger.LogInformation("[MockCalendar] PushEvent '{Title}' for user {UserId} → {ExternalId}",
            calendarEvent.Title, userId, externalId);
        return Task.FromResult(externalId);
    }

    public Task UpdateEventAsync(int userId, int integrationId, string externalEventId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        logger.LogInformation("[MockCalendar] UpdateEvent {ExternalId} '{Title}' for user {UserId}",
            externalEventId, calendarEvent.Title, userId);
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(int userId, int integrationId, string externalEventId, CancellationToken ct = default)
    {
        logger.LogInformation("[MockCalendar] DeleteEvent {ExternalId} for user {UserId}", externalEventId, userId);
        return Task.CompletedTask;
    }

    public Task<List<CalendarFreeBusy>> GetFreeBusyAsync(int userId, int integrationId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        logger.LogInformation("[MockCalendar] GetFreeBusy {Start} - {End} for user {UserId}", start, end, userId);
        return Task.FromResult(new List<CalendarFreeBusy>());
    }

    public Task<CalendarSyncResult> SyncEventsAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        logger.LogInformation("[MockCalendar] SyncEvents for user {UserId}", userId);
        return Task.FromResult(new CalendarSyncResult(0, 0, 0, null));
    }

    public Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        logger.LogInformation("[MockCalendar] TestConnection for user {UserId}", userId);
        return Task.FromResult(true);
    }
}
