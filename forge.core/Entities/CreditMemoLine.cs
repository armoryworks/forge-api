using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>⚡ ACCOUNTING BOUNDARY — one line of a credit memo.</summary>
public class CreditMemoLine : BaseEntity
{
    public int CreditMemoId { get; set; }

    public int LineNumber { get; set; }

    public CreditMemoLineType LineType { get; set; } = CreditMemoLineType.Item;

    public int? PartId { get; set; }

    /// <summary>The invoice line credited. Credited quantity per invoice line never exceeds the invoiced quantity.</summary>
    public int? InvoiceLineId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>"NON" for a non-taxable line; null or "TAX" for taxable. Write-off lines are never taxed.</summary>
    public string? TaxCode { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    /// <summary>The line's effect on the credit: a restocking fee reduces it.</summary>
    public decimal SignedTotal => LineType == CreditMemoLineType.RestockingFee ? -LineTotal : LineTotal;

    public bool IsTaxable => LineType != CreditMemoLineType.WriteOff && TaxCode != InvoiceLine.NonTaxableCode;

    public CreditMemo CreditMemo { get; set; } = null!;
    public Part? Part { get; set; }
    public InvoiceLine? InvoiceLine { get; set; }
}
