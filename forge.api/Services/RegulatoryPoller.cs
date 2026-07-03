using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Regulatory;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>regulatory-watchtower: polls active sources and creates Pending proposals.</summary>
public interface IRegulatoryPoller
{
    Task<int> PollActiveAsync(CancellationToken ct = default);
}

/// <summary>
/// regulatory-watchtower. Polls every active source via <see cref="IRegulatoryFeedClient"/> and
/// records new items as <b>Pending</b> proposals (deduped by source+title). Propose-only — an
/// admin later applies/dismisses; nothing is auto-applied to the compliance calendar.
/// </summary>
public sealed class RegulatoryPoller(AppDbContext db, IRegulatoryFeedClient client, IClock clock) : IRegulatoryPoller
{
    public async Task<int> PollActiveAsync(CancellationToken ct = default)
    {
        var sources = await db.RegulatorySources.Where(s => s.IsActive).ToListAsync(ct);
        var created = 0;

        foreach (var source in sources)
        {
            var items = await client.FetchAsync(source, ct);
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
        return created;
    }
}
