using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities;

/// <summary>
/// A piece rate as a TIMELINE, not a number: the pay per good piece for a part
/// (optionally one operation), effective-dated. Changing a rate closes the open
/// row and opens a new one — work always resolves the rate as-of the day it was
/// performed, so history never rewrites and mixed-rate weeks fall out correctly.
/// </summary>
public class PieceRate : BaseAuditableEntity
{
    public int PartId { get; set; }

    /// <summary>Optional: rate for one routing operation; null = the whole part.</summary>
    public int? OperationId { get; set; }

    public decimal RatePerPiece { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null while current; set (day before the successor starts) when superseded.</summary>
    public DateOnly? EffectiveTo { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }

    [ForeignKey(nameof(OperationId))]
    public Operation? Operation { get; set; }
}
