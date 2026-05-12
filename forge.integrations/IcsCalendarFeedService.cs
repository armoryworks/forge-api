using System.Text;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class IcsCalendarFeedService(ILogger<IcsCalendarFeedService> logger) : ICalendarIntegrationService
{
    public string ProviderId => "ics_feed";

    public Task<string> PushEventAsync(int userId, int integrationId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // ICS feed is read-only — events are generated from QB data, not pushed
        var uid = $"qb-{Guid.NewGuid():N}@forge.local";
        logger.LogInformation("ICS Feed: Generated UID {Uid} for event '{Title}'", uid, calendarEvent.Title);
        return Task.FromResult(uid);
    }

    public Task UpdateEventAsync(int userId, int integrationId, string externalEventId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        logger.LogInformation("ICS Feed: Event {ExternalId} updated in feed", externalEventId);
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(int userId, int integrationId, string externalEventId, CancellationToken ct = default)
    {
        logger.LogInformation("ICS Feed: Event {ExternalId} removed from feed", externalEventId);
        return Task.CompletedTask;
    }

    public Task<List<CalendarFreeBusy>> GetFreeBusyAsync(int userId, int integrationId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        // ICS feed is outbound only — no free/busy query
        return Task.FromResult(new List<CalendarFreeBusy>());
    }

    public Task<CalendarSyncResult> SyncEventsAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        return Task.FromResult(new CalendarSyncResult(0, 0, 0, null));
    }

    public Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public static string GenerateIcs(IEnumerable<CalendarEvent> events, string calendarName = "QB Engineer")
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine($"PRODID:-//QB Engineer//Calendar//EN");
        sb.AppendLine($"X-WR-CALNAME:{calendarName}");

        foreach (var evt in events)
        {
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{evt.ExternalId ?? Guid.NewGuid().ToString("N")}@forge.local");
            sb.AppendLine($"DTSTART:{evt.StartTime.UtcDateTime:yyyyMMddTHHmmssZ}");
            sb.AppendLine($"DTEND:{evt.EndTime.UtcDateTime:yyyyMMddTHHmmssZ}");
            sb.AppendLine($"SUMMARY:{EscapeIcs(evt.Title)}");

            if (!string.IsNullOrWhiteSpace(evt.Description))
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(evt.Description)}");

            if (!string.IsNullOrWhiteSpace(evt.Location))
                sb.AppendLine($"LOCATION:{EscapeIcs(evt.Location)}");

            sb.AppendLine($"DTSTAMP:{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");
        return sb.ToString();
    }

    private static string EscapeIcs(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
