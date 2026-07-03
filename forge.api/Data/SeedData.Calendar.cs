using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Calendar;
using Forge.Data.Context;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// compliance-calendar A-1, Stage 1b. Seeds the baseline calendar taxonomy — the
    /// Super-Groups and the four Event-Types promoted from the legacy <c>EventType</c>
    /// enum (Meeting/Training/Safety/Other) — then backfills existing events'
    /// <c>EventTypeId</c> from their legacy enum value. Idempotent; safe every boot.
    /// The richer seeded compliance buckets (OSHA/EPA/ATF/…) land in Stage 6.
    /// </summary>
    public static async Task SeedCalendarTaxonomyAsync(AppDbContext db)
    {
        var groups = new[]
        {
            new CalendarSuperGroup { Key = "general", Name = "General", Color = "#0ea5e9", DefaultVisible = true, SortOrder = 10, IsSystem = true },
            new CalendarSuperGroup { Key = "safety", Name = "Safety", Color = "#f59e0b", DefaultVisible = true, SortOrder = 20, IsSystem = true },
            new CalendarSuperGroup { Key = "people", Name = "People & HR", Color = "#8b5cf6", DefaultVisible = true, SortOrder = 30, IsSystem = true },
        };
        foreach (var g in groups)
        {
            if (!await db.CalendarSuperGroups.AnyAsync(x => x.Key == g.Key))
                db.CalendarSuperGroups.Add(g);
        }
        await db.SaveChangesAsync();

        var groupIds = await db.CalendarSuperGroups.ToDictionaryAsync(g => g.Key, g => g.Id);

        // (key, name, parent super-group key) — keys are the lower-cased legacy enum names.
        var types = new[]
        {
            ("meeting", "Meeting", "general"),
            ("training", "Training", "people"),
            ("safety", "Safety", "safety"),
            ("other", "Other", "general"),
        };
        var order = 1;
        foreach (var (key, name, groupKey) in types)
        {
            if (!await db.CalendarEventTypes.AnyAsync(x => x.Key == key) && groupIds.TryGetValue(groupKey, out var gid))
            {
                db.CalendarEventTypes.Add(new CalendarEventType
                {
                    SuperGroupId = gid,
                    Key = key,
                    Name = name,
                    SortOrder = order,
                    IsSystem = true,
                });
            }
            order++;
        }
        await db.SaveChangesAsync();

        // Backfill existing events from the legacy enum. Idempotent — fills only NULLs.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE events SET event_type_id = ct.id " +
            "FROM calendar_event_types ct " +
            "WHERE events.event_type_id IS NULL AND lower(events.event_type) = ct.key");
    }
}
