namespace Forge.Core.Entities;

/// <summary>
/// C3 — an admin-defined, reusable customer grouping (a saved named filter). Replaces the
/// hard-coded example segments on the segments page. <see cref="FilterCriteria"/> holds the
/// optional saved filter (JSON) the UI applies to scope the customer list; the entity itself
/// is the named, persisted definition + its CRUD.
/// </summary>
public class CustomerSegment : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FilterCriteria { get; set; }
    public bool IsActive { get; set; } = true;
}
