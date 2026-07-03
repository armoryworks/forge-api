namespace Forge.Core.Entities.Calendar;

/// <summary>
/// Top of the calendar taxonomy (compliance-calendar A-1): an Event belongs to exactly
/// one <see cref="CalendarEventType"/>, which belongs to exactly one Super-Group.
/// The Super-Group is the visibility boundary (A-2) and carries the tiered-behaviour
/// flag (A-4). Multi-group appearance is a deliberate duplicate, never multi-membership.
/// </summary>
public class CalendarSuperGroup : BaseAuditableEntity
{
    /// <summary>Stable slug (e.g. "compliance-osha"); unique.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Hex colour for the calendar layer.</summary>
    public string? Color { get; set; }

    public string? IconName { get; set; }

    /// <summary>
    /// Operational groups default visible; compliance/regulated groups default hidden (A-2).
    /// Actual per-role visibility is governed by SuperGroupRoleVisibility (Stage 1c).
    /// </summary>
    public bool DefaultVisible { get; set; }

    /// <summary>
    /// When true, events under this group are workflow objects (status/owner/evidence),
    /// not just reminders — the tiered model (A-4). Can also be set per event-type.
    /// </summary>
    public bool RequiresTracking { get; set; }

    /// <summary>
    /// Nullable industry key gating this group (e.g. ATF/FDA gated on the shop's declared
    /// industry). Null = always available.
    /// </summary>
    public string? IndustryGate { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Seeded/system group — protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public ICollection<CalendarEventType> EventTypes { get; set; } = [];
}
