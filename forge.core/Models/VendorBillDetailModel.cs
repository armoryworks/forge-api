namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — full detail projection of a <c>VendorBill</c> (header + lines), the AP counterpart
/// of the invoice detail. The list endpoint keeps the lighter <see cref="VendorBillListItemModel"/>; this shape
/// backs the bill detail screen (lines, PO linkage, currency/booking rate).
/// </summary>
public record VendorBillDetailModel(
    int Id,
    string BillNumber,
    int VendorId,
    string VendorName,
    string? VendorInvoiceNumber,
    int? PurchaseOrderId,
    string Status,
    DateTimeOffset BillDate,
    DateTimeOffset DueDate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    int CurrencyId,
    decimal FxRate,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VendorBillLineDetailModel> Lines,
    // True when any payment applied to this bill has a LATEST bank transmission in Failed status.
    bool HasFailedTransmission = false,
    // Set when the bill was auto-promoted from a vendor-settled expense approval — the UI shows a
    // "from expense" chip and links back; such bills are voided via the expense, not directly.
    int? SourceExpenseId = null);
