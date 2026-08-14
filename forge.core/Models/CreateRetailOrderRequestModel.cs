using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// A consumer order. Used by manual retail entry (walk-in, phone, trade show)
/// and by channel importers alike — one code path, so an imported order and a
/// hand-keyed one are the same kind of thing.
/// </summary>
public record CreateRetailOrderRequestModel
{
    /// <summary>Null uses the install's default channel, which is always account business — so retail callers should always set it.</summary>
    public int? ChannelId { get; init; }

    public RetailBuyerInput Buyer { get; init; } = new();
    public OrderShipToInput ShipTo { get; init; } = new();
    public List<CreateRetailOrderLineModel> Lines { get; init; } = [];

    /// <summary>The channel's order number. Doubles as the idempotency key for imports.</summary>
    public string? ExternalOrderNumber { get; init; }

    /// <summary>The channel's internal order id, when it differs from the human-facing number.</summary>
    public string? ExternalOrderId { get; init; }

    public decimal TaxRate { get; init; }

    /// <summary>Omit to inherit the channel's default. Marketplace means the tax is a pass-through, not our payable.</summary>
    public TaxCollectedBy? TaxCollectedBy { get; init; }

    /// <summary>When the buyer placed the order on the channel. Defaults to now for manual entry.</summary>
    public DateTimeOffset? OrderDate { get; init; }

    public string? Notes { get; init; }

    /// <summary>Shipping charged to the buyer, added as its own order line when non-zero.</summary>
    public decimal? ShippingAmount { get; init; }
}
