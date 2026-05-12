namespace Forge.Core.Entities;

/// <summary>
/// Tenant-configurable rollup role for small shops where one person wears
/// many hats (e.g., a "FrontOffice" template that grants Office Manager +
/// Controller + IT Admin permissions).
///
/// When a user has a template assigned, the auth path expands the template
/// into JWT role claims so downstream policy/[Authorize] checks see the
/// underlying roles as if individually assigned. Phase 3 / WU-06 / C1.
/// </summary>
public class RoleTemplate : BaseAuditableEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// True for out-of-the-box templates seeded at install. System defaults
    /// are protected from edit/delete via the API surface.
    /// </summary>
    public bool IsSystemDefault { get; set; }

    /// <summary>
    /// JSON array of underlying role names — e.g.,
    /// <c>["OfficeManager","Controller","IT Admin"]</c>. Stored as a
    /// serialized string in the column <c>included_role_names</c>; the
    /// API/handler layer is responsible for (de)serialization.
    /// </summary>
    public string IncludedRoleNamesJson { get; set; } = "[]";

    /// <summary>
    /// Soft-deactivation timestamp; templates are never hard-deleted so
    /// audit history stays intact when a previously-assigned template is
    /// retired. The list endpoint filters where this is null.
    /// </summary>
    public DateTimeOffset? DeactivatedAt { get; set; }
}
