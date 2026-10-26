namespace Forge.Core.Models.Accounting;

/// <summary>
/// One purchase order's freight accrual in FREIGHT_CLEARING (functional currency): what its receipts accrued,
/// what freight bills cleared, what closing the PO released to freight-in, and what is still open.
/// </summary>
public sealed class GrniFreightClearingRow
{
    public int PurchaseOrderId { get; init; }
    public string PoNumber { get; init; } = string.Empty;
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal Accrued { get; init; }
    public decimal Billed { get; init; }
    public decimal Released { get; init; }
    public decimal OpenAmount => Accrued - Billed - Released;
}
