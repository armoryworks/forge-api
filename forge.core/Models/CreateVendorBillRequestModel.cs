namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — request to create a Draft <c>VendorBill</c> (AP counterpart of
/// <c>CreateInvoiceRequestModel</c>). Creating a Draft is NOT a posting trigger — approval is.
/// </summary>
public record CreateVendorBillRequestModel(
    int VendorId,
    string? VendorInvoiceNumber,
    int? PurchaseOrderId,
    DateTimeOffset BillDate,
    DateTimeOffset DueDate,
    decimal TaxAmount,
    string? Notes,
    List<CreateVendorBillLineModel> Lines);

/// <summary>
/// A line on a new vendor bill. <c>AccountDeterminationKey</c> is the GL key the line debits when
/// the bill is posted (default <c>OPERATING_EXPENSE</c> when null/blank).
/// </summary>
public record CreateVendorBillLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? AccountDeterminationKey,
    // STAGE-D 3-way match: the PO line this bill line matches (required when the bill is PO-linked).
    int? PurchaseOrderLineId = null);
