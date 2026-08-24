using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities;

/// <summary>
/// Pieces a worker completed on a date, paid at the rate in force that day.
/// The resolved rate row is pinned (FK + snapshot) so pay disputes replay
/// exactly what was in force — earnings never change when rates do.
/// </summary>
public class PieceWorkEntry : BaseAuditableEntity
{
    public int UserId { get; set; }

    public int PartId { get; set; }

    public int? OperationId { get; set; }

    /// <summary>The timeline row in force on <see cref="WorkDate"/>.</summary>
    public int PieceRateId { get; set; }

    public DateOnly WorkDate { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>The rate as resolved at entry time (immutable evidence).</summary>
    public decimal RateSnapshot { get; set; }

    /// <summary>Quantity × rate snapshot, rounded to cents at entry time.</summary>
    public decimal Earnings { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }

    [ForeignKey(nameof(PartId))]
    public Part? Part { get; set; }

    [ForeignKey(nameof(PieceRateId))]
    public PieceRate? PieceRate { get; set; }
}
