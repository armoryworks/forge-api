namespace Forge.Core.Entities.Calendar;

/// <summary>
/// compliance-calendar A-3: a saved layer-selection over the overlay calendar. A personal
/// view (<see cref="OwnerUserId"/> set) or a role-default (<see cref="RoleKey"/> set,
/// owner null). <see cref="Scope"/> is "master" or "module:&lt;area&gt;" for the
/// module-embedded scoped calendars (e.g. the compliance module's own calendar).
/// </summary>
public class CalendarSavedView : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Set for a personal view; null for a role-default / system view.</summary>
    public int? OwnerUserId { get; set; }

    /// <summary>Set for a role-default view; null for a personal view.</summary>
    public string? RoleKey { get; set; }

    /// <summary>"master" or "module:&lt;area&gt;" (e.g. "module:compliance").</summary>
    public string Scope { get; set; } = "master";

    /// <summary>Super-Group layers turned on in this view.</summary>
    public int[] SelectedSuperGroupIds { get; set; } = [];

    /// <summary>Optional finer Event-Type layer selection.</summary>
    public int[] SelectedEventTypeIds { get; set; } = [];

    /// <summary>The default view for its owner/role + scope.</summary>
    public bool IsDefault { get; set; }
}
