using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// Phase 3 H4 / WU-20 — immutable BOM revision snapshot.
///
/// BOMs in this codebase are part-scoped (BOMLine rows hang directly off
/// Part). Prior to this WU there was no immutable history of a BOM's state
/// at any given moment — modifying a BOMLine overwrote the prior data, so
/// a Job released against an earlier BOM state could not be reconstructed
/// historically. Compliance positioning (food / medical / aerospace /
/// automotive) requires that.
///
/// A <see cref="BomRevision"/> captures the full set of components for a
/// part's BOM at one point in time. <see cref="Part.CurrentBomRevisionId"/>
/// points at the active revision; older revisions remain readable but
/// immutable. Auto-revision rule: any component-list change creates a new
/// revision (handled in the BOM mutation handlers, not in EF triggers).
/// </summary>
public class BomRevision : BaseAuditableEntity
{
    /// <summary>The part whose BOM this revision belongs to.</summary>
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;

    /// <summary>1, 2, 3, ... — monotonically increasing per part.</summary>
    public int RevisionNumber { get; set; }

    /// <summary>
    /// When this revision became (or becomes) the effective BOM. Defaults
    /// to <c>CreatedAt</c> at construction time.
    /// </summary>
    public DateTimeOffset EffectiveDate { get; set; }

    /// <summary>Free-text revision notes (e.g., ECO id, change summary).</summary>
    public string? Notes { get; set; }

    /// <summary>User who created this revision (not the parent part).</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>Immutable component snapshot — never edited after creation.</summary>
    public ICollection<BomRevisionLine> Entries { get; set; } = [];
}
