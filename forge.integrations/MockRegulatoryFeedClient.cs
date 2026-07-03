using Forge.Core.Entities.Regulatory;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// regulatory-watchtower: offline/air-gap-safe mock feed client. Returns nothing so the poller
/// is a no-op without outbound internet. Real per-feed-type clients (API/RSS/email/scrape) are a
/// network-dependent follow-up registered in place of this when a Watchtower node is online.
/// </summary>
public sealed class MockRegulatoryFeedClient : IRegulatoryFeedClient
{
    public Task<IReadOnlyList<RegulatoryFeedItem>> FetchAsync(RegulatorySource source, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RegulatoryFeedItem>>([]);
}
