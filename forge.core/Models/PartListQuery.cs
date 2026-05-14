
namespace Forge.Core.Models;

/// <summary>
/// Query parameters for <c>GET /api/v1/parts</c>. Phase 3 F7-partial / WU-17
/// standardises pagination, sort, search, and the part-specific filters
/// around a single bound model.
///
/// Pre-beta: the legacy <c>type=Assembly|RawMaterial|...</c> filter was
/// dropped along with the underlying PartType column. Filter by the three
/// axes instead — see <see cref="ProcurementSource"/> + <see cref="InventoryClass"/>.
/// </summary>
public record PartListQuery : PagedQuery
{
    /// <summary>Lifecycle status (Active / Draft / Prototype / Obsolete).</summary>
    public PartStatus? Status { get; init; }

    /// <summary>Activation alias — <c>true</c> excludes Obsolete; <c>false</c> shows Obsolete only.</summary>
    public bool? IsActive { get; init; }

    /// <summary>Pillar 1 axis filter — Make / Buy / Subcontract / Phantom.</summary>
    public ProcurementSource? ProcurementSource { get; init; }

    /// <summary>Pillar 1 axis filter — Raw / Component / Subassembly / FinishedGood / Consumable / Tool.</summary>
    public InventoryClass? InventoryClass { get; init; }

    /// <summary>Restrict to parts whose preferred-vendor matches this id.</summary>
    public int? DefaultVendorId { get; init; }
}
