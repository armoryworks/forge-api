
namespace Forge.Core.Entities;

/// <summary>
/// Phase 3 H4 / WU-20 — one component line in an immutable
/// <see cref="BomRevision"/> snapshot. Mirrors the Part-side
/// <see cref="BOMEntry"/> shape but is frozen at revision-creation time.
/// </summary>
public class BomRevisionEntry : BaseAuditableEntity
{
    public int BomRevisionId { get; set; }
    public BomRevision BomRevision { get; set; } = null!;

    /// <summary>The child part consumed by the parent at this revision.</summary>
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>UoM as a string (snapshot of UnitOfMeasure name at the time).</summary>
    public string UnitOfMeasure { get; set; } = string.Empty;

    /// <summary>Optional — which routing operation consumes this material.</summary>
    public int? OperationId { get; set; }

    public string? ReferenceDesignator { get; set; }
    public BOMSourceType SourceType { get; set; } = BOMSourceType.Buy;
    public int? LeadTimeDays { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}
