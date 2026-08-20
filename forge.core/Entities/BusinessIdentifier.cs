using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One human-readable business number (part number, order/invoice number, customer code, …) with a
/// validity window. Exactly one row per entity is active (<see cref="EffectiveTo"/> == null); a rename
/// closes the current row and opens a new one, so a retired number still resolves to its owner and the
/// full history is queryable. The entity's own denormalized number column mirrors the active value.
/// </summary>
public class BusinessIdentifier : BaseAuditableEntity
{
    public BusinessEntityType EntityType { get; set; }
    public int EntityId { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Null while active; set to the rename time when superseded.</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>DB-computed (STORED) mirror of <c>effective_to IS NULL</c>. Read-only — the app never sets
    /// it; the partial unique index on active rows is enforced off it.</summary>
    public bool IsActive { get; private set; }
}
