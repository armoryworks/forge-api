using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Core.Entities.Regulatory;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>regulatory-watchtower: polls active sources and creates Pending proposals.</summary>
public interface IRegulatoryPoller
{
    Task<RegulatoryPollResult> PollActiveAsync(CancellationToken ct = default);
}

/// <summary>regulatory-watchtower: how many sources were active, how many could not be fetched, and how many new proposals a poll created.</summary>
public sealed record RegulatoryPollResult(int ActiveSources, int Created, int FailedSources = 0);

/// <summary>
/// regulatory-watchtower. Polls every active source via <see cref="IRegulatoryFeedClient"/> and
/// records new items as <b>Pending</b> proposals (deduped by source+title). Propose-only — an
/// admin later applies/dismisses; nothing is auto-applied to the compliance calendar.
/// A source whose fetch fails is counted in <see cref="RegulatoryPollResult.FailedSources"/>, keeps its
/// previous LastPolledAt, and does not stop the other sources from being polled.
/// </summary>
public sealed class RegulatoryPoller(
    AppDbContext db,
    IRegulatoryFeedClient client,
    IClock clock,
    ILogger<RegulatoryPoller>? logger = null) : IRegulatoryPoller
{
    private readonly ILogger<RegulatoryPoller> _logger = logger ?? NullLogger<RegulatoryPoller>.Instance;

    public async Task<RegulatoryPollResult> PollActiveAsync(CancellationToken ct = default)
    {
        var sources = await db.RegulatorySources.Where(s => s.IsActive).ToListAsync(ct);
        var created = 0;
        var failed = 0;

        foreach (var source in sources)
        {
            IReadOnlyList<RegulatoryFeedItem> items;
            try
            {
                items = await client.FetchAsync(source, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Regulatory source {SourceId} ({SourceName}) could not be polled", source.Id, source.Name);
                failed++;
                continue;
            }

            foreach (var item in items)
            {
                var exists = await db.RegulatoryChangeProposals
                    .AnyAsync(p => p.RegulatorySourceId == source.Id && p.Title == item.Title, ct);
                if (exists)
                    continue;

                db.RegulatoryChangeProposals.Add(new RegulatoryChangeProposal
                {
                    RegulatorySourceId = source.Id,
                    Title = item.Title,
                    SummaryUrl = item.Url,
                    Details = item.Details,
                    Status = RegulatoryProposalStatus.Pending,
                });
                created++;
            }
            source.LastPolledAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new RegulatoryPollResult(sources.Count, created, failed);
    }
}
