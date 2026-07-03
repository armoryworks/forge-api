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

    /// <summary>
    /// compliance-calendar A-5, Stage 6. Seeds the regulatory compliance Super-Groups and a
    /// starter set of Event-Types. All default-hidden (A-2) and tracking-tier (A-4);
    /// ATF/FDA carry an <c>IndustryGate</c> so they only surface for the declared industry.
    /// Idempotent; safe every boot.
    /// </summary>
    public static async Task SeedComplianceBucketsAsync(AppDbContext db)
    {
        // (key, name, colour, industry gate)
        var groups = new (string Key, string Name, string Color, string? Industry)[]
        {
            ("safety-osha", "OSHA / Safety", "#dc2626", null),
            ("environmental-epa", "EPA / Environmental", "#16a34a", null),
            ("hazmat-dot", "DOT / Hazmat", "#ea580c", null),
            ("tax-corporate", "Tax & Corporate", "#7c3aed", null),
            ("hr-employment", "HR / Employment", "#0891b2", null),
            ("fire-facilities", "Fire / Facilities", "#b91c1c", null),
            ("atf-firearms", "ATF (Firearms)", "#374151", "firearms"),
            ("fda", "FDA", "#0d9488", "food-medical"),
        };
        var order = 100;
        foreach (var g in groups)
        {
            if (!await db.CalendarSuperGroups.AnyAsync(x => x.Key == g.Key))
            {
                db.CalendarSuperGroups.Add(new CalendarSuperGroup
                {
                    Key = g.Key,
                    Name = g.Name,
                    Color = g.Color,
                    DefaultVisible = false,
                    RequiresTracking = true,
                    IndustryGate = g.Industry,
                    SortOrder = order,
                    IsSystem = true,
                });
            }
            order += 10;
        }
        await db.SaveChangesAsync();

        var groupIds = await db.CalendarSuperGroups.ToDictionaryAsync(g => g.Key, g => g.Id);

        // (type key, name, parent group key) — starter regulatory event types.
        var types = new (string Key, string Name, string GroupKey)[]
        {
            ("osha-300a", "OSHA 300A posting", "safety-osha"),
            ("osha-inspection", "OSHA inspection", "safety-osha"),
            ("epa-tier-ii", "EPA Tier II inventory", "environmental-epa"),
            ("i-9", "Form I-9 verification", "hr-employment"),
            ("eeo-1", "EEO-1 report", "hr-employment"),
            ("fire-marshal", "Fire-marshal inspection", "fire-facilities"),
            ("atf-afmer", "ATF AFMER", "atf-firearms"),
            ("est-tax", "Quarterly estimated tax", "tax-corporate"),
        };
        var typeOrder = 100;
        foreach (var t in types)
        {
            if (!await db.CalendarEventTypes.AnyAsync(x => x.Key == t.Key) && groupIds.TryGetValue(t.GroupKey, out var gid))
            {
                db.CalendarEventTypes.Add(new CalendarEventType
                {
                    SuperGroupId = gid,
                    Key = t.Key,
                    Name = t.Name,
                    RequiresTracking = true,
                    SortOrder = typeOrder,
                    IsSystem = true,
                });
            }
            typeOrder += 10;
        }
        await db.SaveChangesAsync();
    }
}
