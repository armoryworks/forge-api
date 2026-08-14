namespace Forge.Core.Models;

public record CreateRetailOrderLineModel
{
    /// <summary>Null for an unmapped listing — the line still imports, carrying <see cref="Description"/> only.</summary>
    public int? PartId { get; init; }

    /// <summary>The channel's SKU, used to resolve <see cref="PartId"/> via ChannelListing when the caller did not.</summary>
    public string? ExternalSku { get; init; }

    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; }

    /// <summary>Price actually paid, as reported by the channel. Never re-resolved from a price list — retail price is whatever the listing sold for.</summary>
    public decimal UnitPrice { get; init; }

    public string? Notes { get; init; }
}
