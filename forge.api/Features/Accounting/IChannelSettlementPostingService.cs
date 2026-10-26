namespace Forge.Api.Features.Accounting;

/// <summary>
/// Posts a marketplace payout batch (<see cref="Forge.Core.Entities.ChannelSettlement"/>) to the GL once it is
/// reconciled or its variance is accepted. The proceeds that settle the orders' open invoices are recorded as a
/// customer payment (method BankTransfer, party = the invoice's customer, i.e. the channel's house account)
/// applied to those invoices, so the invoices read Paid and the payment posts Dr CASH / Cr AR_CONTROL through
/// <see cref="IPaymentCashPostingService"/>; voiding that payment reverses it like any other. The settlement's
/// own journal carries the rest of the payout: Dr CASH for the remainder, Dr MARKETPLACE_FEES for the channel's
/// fees, Dr SALES_TAX_PAYABLE for tax the marketplace withheld and remits, Dr SALES_RETURNS for refunds, the
/// MARKETPLACE_RESERVE account for reserve movements, and Cr CUSTOMER_DEPOSITS for proceeds with no open
/// invoice to settle (order not yet invoiced or never imported). An accepted variance lands on
/// MARKETPLACE_FEES. A no-op while CAP-ACCT-FULLGL is off.
/// </summary>
public interface IChannelSettlementPostingService
{
    /// <summary>
    /// Posts the settlement's journal. Idempotent by settlement id: a second call returns without posting
    /// or relieving anything again. Skipped (with a warning) when the batch is not in the book's functional
    /// currency.
    /// </summary>
    /// <param name="settlementId">The reconciled or accepted settlement.</param>
    /// <param name="actingUserId">Server-trusted actor (recorded as PostedBy).</param>
    Task PostSettlementAsync(int settlementId, int actingUserId, CancellationToken ct = default);

    /// <summary>The ids, among <paramref name="settlementIds"/>, whose journal or payout payment has already been posted.</summary>
    Task<IReadOnlySet<int>> PostedSettlementIdsAsync(IReadOnlyCollection<int> settlementIds, CancellationToken ct = default);
}
