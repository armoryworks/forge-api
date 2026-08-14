namespace Forge.Core.Models;

/// <summary>
/// Retail-buyer list filters. Inherits paging plus the free-text <c>Q</c>,
/// which matches display name, contact email and external buyer id.
/// </summary>
public record RetailBuyerListQuery : PagedQuery
{
    /// <summary>Restrict to one channel. Null spans every retail channel.</summary>
    public int? ChannelId { get; init; }

    /// <summary>True returns only buyers whose PII has already been scrubbed; false only un-scrubbed ones.</summary>
    public bool? IsPurged { get; init; }
}
