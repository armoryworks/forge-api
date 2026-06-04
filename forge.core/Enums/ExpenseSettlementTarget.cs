namespace Forge.Core.Enums;

/// <summary>
/// How an approved <see cref="Forge.Core.Entities.Expense"/> settles, for the
/// Phase-1 STAGE C GL posting (ACCOUNTING_SUITE_PLAN §7 matrix row "Expense
/// approved": <c>Dr Expense / Cr AP (party required) or Cr Cash</c>). This is the
/// minimal additive disambiguator the plan calls for (§6 Phase-1 row: "add
/// settlement-target + Vendor FK on Expense").
///
/// <para>It is <b>operationally inert</b>: it carries no behavior outside the
/// GL posting path, which is itself dark until CAP-ACCT-FULLGL is enabled. When
/// unset (null on the entity) the posting service falls back to a Cash credit
/// unless a <see cref="Forge.Core.Entities.Expense.VendorId"/> is present (which
/// implies AP) — see <c>ExpenseApPostingService</c>.</para>
/// </summary>
public enum ExpenseSettlementTarget
{
    /// <summary>
    /// The expense is owed to a vendor and accrues to Accounts Payable
    /// (Dr Expense / Cr AP, party = the vendor). Requires a vendor party.
    /// </summary>
    AccountsPayable,

    /// <summary>
    /// The expense was paid (or is treated as paid) directly — out-of-pocket /
    /// card / petty cash — and credits Cash (Dr Expense / Cr Cash). No party.
    /// </summary>
    Cash,
}
