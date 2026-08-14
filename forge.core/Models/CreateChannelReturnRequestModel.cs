namespace Forge.Core.Models;

/// <summary>
/// A return authorised on a sales channel. Distinct from
/// <see cref="CreateCustomerReturnRequestModel"/> because a channel return
/// carries no job, arrives already approved by the platform, and is identified
/// by an order line plus the platform's own RMA id.
/// </summary>
public record CreateChannelReturnRequestModel
{
    public int SalesOrderId { get; init; }

    /// <summary>Omit on a single-line order — the line is unambiguous and the handler resolves it.</summary>
    public int? SalesOrderLineId { get; init; }

    /// <summary>The platform's RMA identifier. Doubles as the idempotency key for connector replays.</summary>
    public string? ExternalRmaId { get; init; }

    public string Reason { get; init; } = string.Empty;
    public string? Notes { get; init; }

    /// <summary>Defaults to the full line quantity when omitted.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>Amount the platform refunded the buyer, so it can be tied to the negative settlement line later.</summary>
    public decimal? RefundAmount { get; init; }

    public DateTimeOffset? ReturnDate { get; init; }
}
