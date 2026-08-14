namespace Forge.Core.Entities;

/// <summary>
/// The mapping between one saleable listing on a <see cref="SalesChannel"/> and
/// the <see cref="Part"/> it fulfils from.
///
/// <para>Replaces <c>ECommerceIntegration.PartMappingsJson</c>. A JSON blob was
/// workable while the only consumer was order import doing a single lookup, but
/// inventory sync has to answer the reverse question — "which listings publish
/// this part, and what quantity did we last push?" — and unmapped-SKU triage
/// has to answer "which listings have no part yet?". Neither is a lookup; both
/// are queries, and a blob cannot serve them.</para>
/// </summary>
public class ChannelListing : BaseAuditableEntity
{
    public int ChannelId { get; set; }
    public SalesChannel Channel { get; set; } = null!;

    /// <summary>The channel's identifier for the listing itself (eBay item id, Etsy listing id, Amazon ASIN).</summary>
    public string ExternalListingId { get; set; } = string.Empty;

    /// <summary>The seller SKU as the channel knows it. This is what arrives on order lines.</summary>
    public string? ExternalSku { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// Null means unmapped: orders for this listing still import, but their
    /// lines land with a null <see cref="SalesOrderLine.PartId"/> and free-text
    /// description so nothing is silently dropped. Unmapped listings are the
    /// triage queue.
    /// </summary>
    public int? PartId { get; set; }
    public Part? Part { get; set; }

    /// <summary>Price the listing currently shows on the channel. Informational — the order carries the price actually paid.</summary>
    public decimal? ListedPrice { get; set; }

    /// <summary>Quantity last published to the channel by inventory sync, so a no-op push can be skipped.</summary>
    public decimal? PublishedQuantity { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
