namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Standalone mode: full CRUD. Integrated mode: read-only cache.
/// </summary>
public class InvoiceLine : BaseEntity
{
    public int InvoiceId { get; set; }
    public int? PartId { get; set; }
    public string Description { get; set; } = string.Empty;
    // Phase 3 / WU-23 (F8-broad): UoM-aware fractional quantities. Was int;
    // promoted to decimal(18,4) so SO/Quote/Invoice round-trips don't truncate.
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int LineNumber { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;

    public Invoice Invoice { get; set; } = null!;
    public Part? Part { get; set; }
}
