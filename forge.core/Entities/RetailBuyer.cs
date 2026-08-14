namespace Forge.Core.Entities;

/// <summary>
/// A consumer who bought through a retail or marketplace
/// <see cref="SalesChannel"/>. Deliberately thin, and deliberately NOT a
/// <see cref="Customer"/>.
///
/// <para><b>Why not a Customer row.</b> <see cref="Customer"/> is a B2B account
/// — credit limits and holds, tax-exemption certificates, price lists, portal
/// logins, ITAR/AS9100 flags, credit-review cadence. None of it applies to
/// someone who bought one item on Etsy, and minting a customer per consumer
/// would bury the few hundred real accounts under thousands of one-shot rows in
/// every picker, segment and "sales by customer" report.</para>
///
/// <para><b>Why it is disposable.</b> Marketplace data-protection terms (Amazon's
/// DPP is the strictest) require buyer PII to be deletable on request and
/// retained no longer than needed to fulfil. Keeping consumers in their own
/// table makes that a scoped purge job; scrubbing them out of the customer
/// master would mean deleting rows other records point at. See
/// <see cref="PurgeAfter"/>.</para>
/// </summary>
public class RetailBuyer : BaseAuditableEntity
{
    public int ChannelId { get; set; }
    public SalesChannel Channel { get; set; } = null!;

    /// <summary>
    /// The channel's own identifier for this buyer — Amazon's buyer id, eBay's
    /// username, Etsy's user id. Unique per channel, and the key an import
    /// matches on to recognize a repeat buyer.
    /// </summary>
    public string ExternalBuyerId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Contact email as supplied by the channel. On marketplaces this is
    /// usually a rotating anonymized relay address, not the buyer's real
    /// mailbox — treat it as a routing token with a short useful life, never as
    /// a stable identity key. Match on <see cref="ExternalBuyerId"/> instead.
    /// </summary>
    public string? ContactEmail { get; set; }

    public string? Phone { get; set; }

    /// <summary>
    /// True only when the channel explicitly reports the buyer opted in to
    /// marketing contact. Defaults false — marketplace buyers have NOT
    /// consented to anything beyond the transaction.
    /// </summary>
    public bool MarketingConsent { get; set; }

    public DateTimeOffset? FirstOrderAt { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
    public int OrderCount { get; set; }

    /// <summary>
    /// When the PII on this row becomes eligible for scrubbing. Set from the
    /// channel's retention window at import; the purge job clears
    /// <see cref="DisplayName"/> / <see cref="ContactEmail"/> / <see cref="Phone"/>
    /// past this date while leaving the row (and therefore the order history
    /// and its analytics) intact. Null = no scheduled purge.
    /// </summary>
    public DateTimeOffset? PurgeAfter { get; set; }

    /// <summary>Set once the PII columns have been scrubbed, so the job is idempotent and the state is auditable.</summary>
    public DateTimeOffset? PurgedAt { get; set; }

    public ICollection<SalesOrder> SalesOrders { get; set; } = [];
}
