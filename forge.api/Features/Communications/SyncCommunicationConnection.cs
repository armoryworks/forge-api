using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Interfaces.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — drive a single connection's polling sync. Resolves the
/// registered <see cref="ICommunicationSyncProvider"/> by ProviderId,
/// invokes <see cref="ICommunicationSyncProvider.SyncRecentAsync(int, CancellationToken)"/>,
/// and stamps <see cref="Core.Entities.CommunicationSyncConfig.LastSyncedAt"/>
/// regardless of whether the provider matched anything (a successful
/// "I checked, nothing new" still counts as a sync for telemetry).
///
/// Returns the count of events the provider produced this round (used
/// only for the user-facing toast — the matcher itself owns the activity-
/// log/contact-interaction writes per event).
///
/// Handler runs from two callers:
///   - Manual user click on the "Sync now" button (HTTP path; CurrentUserId
///     populated by middleware; we still use the connection row's UserId
///     so an admin can sync someone else's row in future without polluting
///     the activity ledger with the admin's identity).
///   - Hangfire recurring job (no HTTP context; CurrentUserId null; the
///     handler reads UserId off the row).
/// </summary>
public record SyncCommunicationConnectionCommand(int Id) : IRequest<SyncCommunicationConnectionResult>;

public record SyncCommunicationConnectionResult(int Id, int EventCount, DateTimeOffset SyncedAt);

public class SyncCommunicationConnectionHandler(
    AppDbContext db,
    IEnumerable<ICommunicationSyncProvider> providers,
    IClock clock,
    ILogger<SyncCommunicationConnectionHandler> logger)
    : IRequestHandler<SyncCommunicationConnectionCommand, SyncCommunicationConnectionResult>
{
    public async Task<SyncCommunicationConnectionResult> Handle(
        SyncCommunicationConnectionCommand request, CancellationToken cancellationToken)
    {
        var connection = await db.CommunicationSyncConfigs
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"CommunicationSyncConfig {request.Id} not found");

        // For HTTP-driven syncs, enforce that the caller owns the row.
        // System-driven (Hangfire) syncs have CurrentUserId=null and are
        // allowed to operate on any row.
        if (db.CurrentUserId is int caller && caller != connection.UserId)
        {
            throw new KeyNotFoundException($"CommunicationSyncConfig {request.Id} not found");
        }

        var provider = providers.FirstOrDefault(p =>
            p.ProviderId == connection.ProviderId && p.Kind == connection.Kind)
            ?? throw new InvalidOperationException(
                $"No registered ICommunicationSyncProvider matches providerId={connection.ProviderId} kind={connection.Kind}. "
                + "Adapter may not be implemented yet (planned providers list it as 'Coming soon').");

        var now = clock.UtcNow;
        int eventCount;
        try
        {
            eventCount = await provider.SyncRecentAsync(connection.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            // Phase 1i — capture the error onto the row so the user-facing
            // connections panel can surface it. We re-load the entity here
            // because the provider may have already mutated and saved
            // (e.g. updated LastSyncedExternalId on partial success). If
            // provider didn't save, our connection ref is the same row.
            logger.LogError(ex,
                "Communication sync failed for connection {ConnectionId} ({Provider}/{Kind})",
                connection.Id, connection.ProviderId, connection.Kind);

            connection.LastError = TrimErrorMessage(ex.Message);
            connection.LastErrorAt = now;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // Success — clear any previous error and stamp LastSyncedAt.
        connection.LastSyncedAt = now;
        connection.LastError = null;
        connection.LastErrorAt = null;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Synced communication connection {ConnectionId} ({Provider}/{Kind}) — {EventCount} events",
            connection.Id, connection.ProviderId, connection.Kind, eventCount);

        return new SyncCommunicationConnectionResult(connection.Id, eventCount, now);
    }

    private static string TrimErrorMessage(string message)
    {
        // Column is varchar(1024); leave headroom for the truncation marker.
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length <= 1000 ? message : message[..1000] + "…";
    }
}
