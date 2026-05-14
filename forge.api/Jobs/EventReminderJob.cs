using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Runs every 15 minutes — sends reminder notifications 30 minutes before events start.
/// </summary>
public class EventReminderJob(
    AppDbContext db,
    IClock clock,
    ILogger<EventReminderJob> logger)
{
    public async Task SendRemindersAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var reminderWindow = now.AddMinutes(30);

        var upcomingEvents = await db.Events
            .Include(e => e.Attendees)
            .Where(e => !e.IsCancelled
                && e.ReminderSentAt == null
                && e.StartTime > now
                && e.StartTime <= reminderWindow)
            .ToListAsync(ct);

        if (upcomingEvents.Count == 0)
        {
            logger.LogDebug("[EventReminder] No events due for reminder");
            return;
        }

        foreach (var evt in upcomingEvents)
        {
            foreach (var attendee in evt.Attendees.Where(a => a.Status != AttendeeStatus.Declined))
            {
                db.Notifications.Add(new Notification
                {
                    UserId = attendee.UserId,
                    Type = "event_reminder",
                    Severity = evt.IsRequired ? "warning" : "info",
                    Source = "events",
                    Title = $"Reminder: {evt.Title}",
                    Message = $"\"{evt.Title}\" starts at {evt.StartTime:hh:mm tt}.{(evt.Location != null ? $" Location: {evt.Location}" : "")}",
                    EntityType = "events",
                    EntityId = evt.Id,
                });
            }

            evt.ReminderSentAt = now;

            logger.LogInformation(
                "[EventReminder] Sent reminder for event {EventId} '{Title}' to {Count} attendees",
                evt.Id, evt.Title, evt.Attendees.Count(a => a.Status != AttendeeStatus.Declined));
        }

        await db.SaveChangesAsync(ct);
    }
}
