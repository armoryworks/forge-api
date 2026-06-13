namespace Forge.Core.Models;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — one bill application of a <see cref="VendorPaymentDetailModel"/>: which bill the
/// payment settled, for how much, and at what settlement FX rate (1 in single-currency).
/// </summary>
public record VendorPaymentApplicationDetailModel(
    int VendorBillId,
    string BillNumber,
    decimal Amount,
    decimal SettlementFxRate);
