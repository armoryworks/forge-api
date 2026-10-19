namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — one line of a <see cref="VendorBillDetailModel"/>. Carries the 3-way-match linkage
/// (<see cref="PurchaseOrderLineId"/>, non-null on PO-matched bills) so the UI can render the matched PO line.
/// <see cref="GlAccountId"/> and <see cref="GlAccountName"/> name the explicit account a standalone line debits,
/// when one was chosen instead of the determination key. <see cref="PurchaseOrderLineLabel"/> names the matched PO
/// line for display ("PO-00012 line 2"), since PO lines carry no line number of their own.
/// </summary>
public record VendorBillLineDetailModel(
    int Id,
    int LineNumber,
    int? PartId,
    int? PurchaseOrderLineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string AccountDeterminationKey,
    int? GlAccountId = null,
    string? GlAccountName = null,
    string? PurchaseOrderLineLabel = null);
