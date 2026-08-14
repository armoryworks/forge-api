namespace Forge.Core.Models;

public record ECommerceOrderLine
{
    /// <summary>Seller SKU as the platform knows it. Resolved to a part through ChannelListing.</summary>
    public string ExternalSku { get; init; } = string.Empty;

    /// <summary>The platform's listing identifier (eBay item id, Etsy listing id, Amazon ASIN).</summary>
    public string? ExternalListingId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    /// <summary>Decimal to match SalesOrderLine — some storefronts sell by weight or length.</summary>
    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }

    /// <summary>Per-line tax, where the platform breaks it out.</summary>
    public decimal? TaxAmount { get; init; }

    /// <summary>Per-line discount, as a positive number.</summary>
    public decimal? DiscountAmount { get; init; }
}
