using Forge.Core.Entities.Regulatory;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// regulatory-watchtower: fetches current items from a regulatory source's feed. Real
/// implementations (Federal Register API, RSS, GovDelivery email, scrape) are network-dependent;
/// the mock returns nothing so the poller is a safe no-op offline / air-gapped.
/// </summary>
public interface IRegulatoryFeedClient
{
    Task<IReadOnlyList<RegulatoryFeedItem>> FetchAsync(RegulatorySource source, CancellationToken ct = default);
}
