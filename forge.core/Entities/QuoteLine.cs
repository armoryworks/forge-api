namespace Forge.Core.Entities;

public class QuoteLine : BaseEntity
{
    public int QuoteId { get; set; }
    public int? PartId { get; set; }
    public string Description { get; set; } = string.Empty;
    // Phase 3 / WU-23 (F8-broad): UoM-aware fractional quantities.
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int LineNumber { get; set; }
    public string? Notes { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;

    public Quote Quote { get; set; } = null!;
    public Part? Part { get; set; }
}
