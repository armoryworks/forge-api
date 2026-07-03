using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// regulated-parts-safety C-3: links a Part to an SDS document (a versioned <see cref="DocumentSet"/>)
/// with SDS metadata. An assembly's Safety tab aggregates + dedupes the SDS set from its BOM
/// materials (computed on-the-fly). Gated by CAP-INV-HAZMAT.
/// </summary>
public class PartSafetyDataSheet : BaseAuditableEntity
{
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;

    /// <summary>The versioned document store holding the SDS PDF(s).</summary>
    public int DocumentSetId { get; set; }

    public SdsType SdsType { get; set; }
    public string? Supplier { get; set; }
    public DateTimeOffset? RevisionDate { get; set; }
}
