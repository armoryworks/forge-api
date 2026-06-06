namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — list/detail projection of a <c>VendorBill</c> (AP counterpart of
/// <c>InvoiceListItemModel</c>). Money fields are the computed bill totals.
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
    DateTimeOffset CreatedAt);
