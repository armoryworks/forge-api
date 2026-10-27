namespace Forge.Core.Enums;

/// <summary>
/// Who raised the memo: a credit memo the vendor sent us, or a debit memo we sent the vendor to claim the amount. Both
/// reduce what we owe the vendor and post the same way. Stored as an integer.
/// </summary>
public enum VendorCreditMemoKind
{
    VendorCredit = 0,
    DebitMemo = 1,
}
