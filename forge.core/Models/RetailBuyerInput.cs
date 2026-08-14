namespace Forge.Core.Models;

/// <summary>
/// Consumer identity supplied with a retail order. Matched to an existing
/// <see cref="Entities.RetailBuyer"/> on (channel, <see cref="ExternalBuyerId"/>)
/// so a repeat buyer accumulates history rather than minting a new row.
/// </summary>
public record RetailBuyerInput
{
    /// <summary>
    /// The channel's identifier for this buyer. For manual entry (walk-in,
    /// phone, trade show) there is no external system, so the caller may leave
    /// this empty and the handler mints a stable synthetic id.
    /// </summary>
    public string? ExternalBuyerId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>On marketplaces this is typically a rotating relay address, not a stable mailbox.</summary>
    public string? ContactEmail { get; init; }

    public string? Phone { get; init; }

    /// <summary>Only set true when the channel explicitly reports an opt-in.</summary>
    public bool MarketingConsent { get; init; }
}
