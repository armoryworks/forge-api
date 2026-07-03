namespace Forge.Core.Entities.Calendar;

/// <summary>
/// compliance-calendar A-2: per-Super-Group role allow-list. A role may see a Super-Group
/// when the group is <see cref="CalendarSuperGroup.DefaultVisible"/> OR an explicit grant
/// row exists here (Admin always sees all). Enforced server-side in Stage 2.
/// </summary>
public class CalendarSuperGroupRoleVisibility : BaseAuditableEntity
{
    public int SuperGroupId { get; set; }
    public CalendarSuperGroup SuperGroup { get; set; } = null!;

    /// <summary>Role key/name granted visibility (e.g. "Manager", "ComplianceOfficer").</summary>
    public string Role { get; set; } = string.Empty;
}
