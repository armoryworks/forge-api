using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Communications;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Wave 8 — recurring Hangfire job that drives <see cref="ICommunicationSyncProvider.SyncRecentAsync"/>
/// for every connected row whose provider supports polling. Runs every 15 min.
///
/// Webhook-driven providers (Twilio, Microsoft Graph push) generally
/// return 0 from SyncRecentAsync — the polling tick is benign for them
/// and we still bump LastSyncedAt for telemetry. The matcher is the
/// single throat both polling and webhook adapters funnel through.
///
/// Failures on a single connection are logged + swallowed so one broken
/// row doesn't starve the rest of the install. The matcher's exception
/// surface is wide (network, IMAP, OAuth refresh, etc.) so the job runs
/// each connection in its own try/catch.
/// </summary>
public class CommunicationSyncJob(
    AppDbContext db,
    IMediator mediator,
    ILogger<CommunicationSyncJob> logger)
{
    public async Task SyncAllConnectedAsync(CancellationToken ct = default)
    {
        var connectionIds = await db.CommunicationSyncConfigs.AsNoTracking()
            .Where(c => c.IsConnected)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (connectionIds.Count == 0)
        {
            logger.LogDebug("CommunicationSyncJob: 0 connected rows; tick skipped");
            return;
        }

        logger.LogInformation("CommunicationSyncJob: starting tick for {Count} connection(s)", connectionIds.Count);

        var totalEvents = 0;
        var failures = 0;
        foreach (var id in connectionIds)
        {
            try
            {
                var result = await mediator.Send(new SyncCommunicationConnectionCommand(id), ct);
                totalEvents += result.EventCount;
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex,
                    "CommunicationSyncJob: connection {ConnectionId} failed; continuing with the rest", id);
            }
        }

        logger.LogInformation(
            "CommunicationSyncJob: tick complete — {Events} events across {Count} connections ({Failures} failed)",
            totalEvents, connectionIds.Count, failures);
    }
}
