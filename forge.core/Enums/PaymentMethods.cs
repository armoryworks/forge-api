namespace Forge.Core.Enums;

/// <summary>
/// Shared predicates over <see cref="PaymentMethod"/>. Single source of truth for "which methods move
/// money electronically through the bank channel" — used by both the transmission-enqueue check
/// (CreateVendorPayment) and the GL cash-in-transit clearing (VendorPaymentCashPostingService), so the
/// two can never drift apart.
/// </summary>
public static class PaymentMethods
{
    /// <summary>
    /// True when the method is submitted electronically to the bank (ACH/wire) — i.e. it gets a
    /// <c>PaymentTransmission</c> and its GL cash credit goes to <c>CASH_IN_TRANSIT</c> until the
    /// transmission settles (architecture.md §7 BANK-002).
    /// </summary>
    public static bool IsElectronic(PaymentMethod method)
        => method is PaymentMethod.BankTransfer or PaymentMethod.Wire;
}
