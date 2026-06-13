namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — one line of a <see cref="VendorBillDetailModel"/>. Carries the 3-way-match linkage
/// (<see cref="PurchaseOrderLineId"/>, non-null on PO-matched bills) so the UI can render the matched PO line.
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
    string AccountDeterminationKey);
