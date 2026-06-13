namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — list/detail projection of a <c>VendorBill</c> (AP counterpart of
/// <c>InvoiceListItemModel</c>). Money fields are the computed bill totals.
/// <paramref name="HasFailedTransmission"/> is true when any payment applied to this bill has a LATEST
/// bank transmission in Failed status — the UI flags the row for manual reprocessing.
/// </summary>
public record VendorBillListItemModel(
    int Id,
    string BillNumber,
    int VendorId,
    string VendorName,
    string? VendorInvoiceNumber,
    string Status,
    DateTimeOffset BillDate,
    DateTimeOffset DueDate,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    DateTimeOffset CreatedAt,
    bool HasFailedTransmission = false,
    // Set when the bill was auto-promoted from a vendor-settled expense approval — the UI shows a
    // "from expense" chip and links back; such bills are voided via the expense, not directly.
    int? SourceExpenseId = null);
