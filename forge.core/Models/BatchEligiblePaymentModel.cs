namespace Forge.Core.Models;

/// <summary>
/// ⚡ BANKING BOUNDARY — an ACH (BankTransfer) vendor payment that is not yet in a live batch.
/// <paramref name="BankAccountId"/> is null when the vendor has no payable bank account
/// (the UI shows "needs verified bank account" and the batch create rejects it).
/// </summary>
public record BatchEligiblePaymentModel(
    int VendorPaymentId,
    string PaymentNumber,
    int VendorId,
    string VendorName,
    decimal Amount,
    DateTimeOffset PaymentDate,
    int? BankAccountId,
    string? BankAccountStatus,
    string? AccountNumberMasked);
