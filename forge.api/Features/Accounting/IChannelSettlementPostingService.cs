namespace Forge.Api.Features.Accounting;

/// <summary>
/// Posts a marketplace payout batch (<see cref="Forge.Core.Entities.ChannelSettlement"/>) to the GL once it is
/// reconciled or its variance is accepted: Dr CASH for the payout, Dr MARKETPLACE_FEES for the channel's fees,
/// Dr SALES_TAX_PAYABLE for tax the marketplace withheld and remits, Dr SALES_RETURNS for refunds, the
/// MARKETPLACE_RESERVE account for reserve movements, and Cr AR_CONTROL (party = the invoice's customer,
/// i.e. the channel's house account) for the settled orders' open invoices. Proceeds with no open invoice
/// to relieve (order not yet invoiced or never imported) credit CUSTOMER_DEPOSITS, and an accepted variance
/// lands on MARKETPLACE_FEES. A no-op while CAP-ACCT-FULLGL is off.
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

    /// <summary>The ids, among <paramref name="settlementIds"/>, whose journal has already been posted.</summary>
    Task<IReadOnlySet<int>> PostedSettlementIdsAsync(IReadOnlyCollection<int> settlementIds, CancellationToken ct = default);
}
