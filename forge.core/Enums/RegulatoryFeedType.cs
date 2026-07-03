namespace Forge.Core.Enums;

/// <summary>
/// regulatory-watchtower (cluster B): how a source is polled. Drives the poller's dispatch —
/// structured APIs are preferred; scrape/email are review-prone (see regulatory-source-inventory).
/// </summary>
public enum RegulatoryFeedType
{
    Api,
    Rss,
    Email,
    Bulk,
    Scrape,
}
