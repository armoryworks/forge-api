using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

using Forge.Api.Capabilities;

namespace Forge.Api.Jobs;

/// <summary>
/// Pushes on-hand quantities out to every mapped, active listing on channels
/// that have inventory sync enabled.
///
/// <para>This is the half of the channel integration that prevents overselling.
/// Order import tells you what sold; without this, the platform keeps offering
/// stock that is already gone and the shop takes orders it cannot fill —
/// which on a marketplace costs seller metrics, not just goodwill.</para>
///
/// <para>Only pushes when the number actually changed. A no-op push still
/// consumes the platform's rate limit, and marketplaces are strict enough about
/// that to throttle a shop that burns its budget re-sending identical values.</para>
/// </summary>
public class ChannelInventorySyncJob(
    AppDbContext db,
    IECommerceServiceFactory connectorFactory,
    IECommerceCredentialProtector protector,
    IClock clock,
    ILogger<ChannelInventorySyncJob> logger,
    ICapabilitySnapshotProvider capabilities)
{
    public async Task SyncAsync(CancellationToken ct = default)
    {
        // ── Capability gate (self-gating job — the VarianceWatchdogJob pattern):
        // channel inventory sync is capability-owned; when the capability is off (services /
        // construction installs) the schedule still ticks but the job is a no-op,
        // so toggling the capability takes effect without a restart.
        if (!capabilities.IsEnabled("CAP-EXT-ECOMMERCE"))
            return;

        var channels = await db.SalesChannels
            .Include(c => c.ECommerceIntegration)
            .Where(c => c.IsActive
                && c.ECommerceIntegrationId != null
                && c.ECommerceIntegration!.IsActive
                && c.ECommerceIntegration.SyncInventory)
            .ToListAsync(ct);

        foreach (var channel in channels)
        {
            ct.ThrowIfCancellationRequested();

            var integration = channel.ECommerceIntegration!;
            if (!connectorFactory.IsSupported(integration.Platform))
            {
                logger.LogDebug(
                    "[CHANNEL-INVENTORY] No connector for {Platform} on channel {Channel} — skipping",
                    integration.Platform, channel.Code);
                continue;
            }

            var connector = connectorFactory.For(integration.Platform);

            // On-hand per mapped part, computed in one grouped query rather than
            // a per-listing lookup.
            var listings = await db.ChannelListings
                .AsNoTracking()
                .Where(l => l.ChannelId == channel.Id && l.IsActive && l.PartId != null)
                .Select(l => new
                {
                    l.ExternalListingId,
                    PartId = l.PartId!.Value,
                    l.PublishedQuantity,
                })
                .ToListAsync(ct);

            if (listings.Count == 0) continue;

            var partIds = listings.Select(l => l.PartId).Distinct().ToList();

            // BinContent is polymorphic (EntityType/EntityId); "part" is the
            // canonical type string. Publish AVAILABLE rather than gross on-hand
            // — reserved stock is already committed to another order, so
            // offering it is the oversell this job exists to prevent.
            var onHandByPart = await db.BinContents
                .AsNoTracking()
                .Where(bc => bc.EntityType == "part"
                    && partIds.Contains(bc.EntityId)
                    && bc.RemovedAt == null)
                .GroupBy(bc => bc.EntityId)
                .Select(g => new
                {
                    PartId = g.Key,
                    Available = g.Sum(x => x.Quantity) - g.Sum(x => x.ReservedQuantity),
                })
                .ToDictionaryAsync(x => x.PartId, x => x.Available, ct);

            var pushed = 0;
            var failed = 0;

            foreach (var listing in listings)
            {
                ct.ThrowIfCancellationRequested();

                var onHand = onHandByPart.GetValueOrDefault(listing.PartId, 0m);
                if (onHand < 0m) onHand = 0m;

                if (listing.PublishedQuantity == onHand) continue;

                try
                {
                    await connector.SyncInventoryAsync(
                        protector.Unprotect(integration.EncryptedCredentials) ?? string.Empty,
                        integration.StoreUrl ?? string.Empty,
                        listing.ExternalListingId,
                        onHand,
                        ct);

                    // Record what was published so the next pass can skip it.
                    await db.ChannelListings
                        .Where(l => l.ChannelId == channel.Id && l.ExternalListingId == listing.ExternalListingId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(l => l.PublishedQuantity, onHand)
                            .SetProperty(l => l.LastSyncedAt, clock.UtcNow), ct);

                    pushed++;
                }
                catch (Exception ex)
                {
                    // One listing failing must not stop the rest — a stale
                    // quantity on the others is the thing being prevented.
                    failed++;
                    logger.LogWarning(ex,
                        "[CHANNEL-INVENTORY] Failed to push quantity for listing {Listing} on channel {Channel}",
                        listing.ExternalListingId, channel.Code);
                }
            }

            if (pushed > 0 || failed > 0)
            {
                logger.LogInformation(
                    "[CHANNEL-INVENTORY] Channel {Channel}: {Pushed} listing(s) updated, {Failed} failed",
                    channel.Code, pushed, failed);
            }
        }
    }
}
