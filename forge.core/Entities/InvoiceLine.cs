using Forge.Core.Enums;

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

    /// <summary>Item (default) or Freight. A freight line has no part, never ships and posts to Freight Income.</summary>
    public DocumentLineType LineType { get; set; } = DocumentLineType.Item;

    public const string NonTaxableCode = "NON";

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    public bool IsTaxable => TaxCode != NonTaxableCode;

    public bool IsFreight => LineType == DocumentLineType.Freight;

    // F-032: per-line taxability code required by QBO AST. Null = inherit invoice-level default.
    // Values: "TAX" (taxable), "NON" (non-taxable). Propagated from Part.TaxCode at invoice creation.
    public string? TaxCode { get; set; }

    public int? SalesOrderLineId { get; set; }

    public Invoice Invoice { get; set; } = null!;
    public Part? Part { get; set; }
    public SalesOrderLine? SalesOrderLine { get; set; }
}
