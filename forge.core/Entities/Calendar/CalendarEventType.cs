namespace Forge.Core.Entities.Calendar;

/// <summary>
/// Middle of the calendar taxonomy (compliance-calendar A-1). Promotes the legacy fixed
/// <c>EventType</c> enum (Meeting/Training/Safety/Other) to a configurable, admin-CRUD
/// table with a single parent Super-Group. Each Event references exactly one of these.
/// </summary>
public class CalendarEventType : BaseAuditableEntity
{
    public int SuperGroupId { get; set; }

    /// <summary>Stable slug (e.g. "meeting", "osha-300a"); unique.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    /// <summary>Type-level tiered flag (A-4); augments the group-level <c>RequiresTracking</c>.</summary>
    public bool RequiresTracking { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Seeded/system type — protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public CalendarSuperGroup SuperGroup { get; set; } = null!;
}
