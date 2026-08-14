using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// Pulls a channel's listings and upserts them into <see cref="ChannelListing"/>.
///
/// <para>Listings are what make an imported order line resolvable to a part, and
/// what inventory sync walks to decide where to push a quantity. Deliberately
/// non-destructive: a listing that disappears from the platform is deactivated,
/// never deleted, because historical order lines still reference the SKU and the
/// part mapping is operator work that should survive the platform hiding a
/// listing for a week.</para>
/// </summary>
public record SyncChannelListingsCommand(int ChannelId) : IRequest<SyncChannelListingsResult>;

public record SyncChannelListingsResult(int Created, int Updated, int Deactivated, int Unmapped);

public class SyncChannelListingsHandler(
    AppDbContext db,
    IECommerceServiceFactory connectorFactory,
    IClock clock,
    ILogger<SyncChannelListingsHandler> logger)
    : IRequestHandler<SyncChannelListingsCommand, SyncChannelListingsResult>
{
    public async Task<SyncChannelListingsResult> Handle(SyncChannelListingsCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels
            .Include(c => c.ECommerceIntegration)
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.ChannelId} not found");

        var integration = channel.ECommerceIntegration
            ?? throw new InvalidOperationException(
                $"Channel '{channel.Code}' has no e-commerce integration attached — nothing to poll.");

        var connector = connectorFactory.For(integration.Platform);

        var polled = await connector.PollListingsAsync(
            integration.EncryptedCredentials, integration.StoreUrl ?? string.Empty, ct);

        // Load the channel's existing listings once and index them, rather than
        // querying per polled row.
        var existing = await db.ChannelListings
            .Where(l => l.ChannelId == channel.Id)
            .ToDictionaryAsync(l => l.ExternalListingId, ct);

        var now = clock.UtcNow;
        var created = 0;
        var updated = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var listing in polled)
        {
            ct.ThrowIfCancellationRequested();
            seen.Add(listing.ExternalListingId);

            if (existing.TryGetValue(listing.ExternalListingId, out var row))
            {
                // Refresh what the platform owns. PartId is NOT touched — that
                // mapping is operator intent, and a title change upstream must
                // never silently unmap a part.
                row.ExternalSku = listing.ExternalSku;
                row.Title = listing.Title;
                row.ListedPrice = listing.Price;
                row.PublishedQuantity = listing.AvailableQuantity;
                row.IsActive = listing.IsActive;
                row.LastSyncedAt = now;
                updated++;
            }
            else
            {
                db.ChannelListings.Add(new ChannelListing
                {
                    ChannelId = channel.Id,
                    ExternalListingId = listing.ExternalListingId,
                    ExternalSku = listing.ExternalSku,
                    Title = listing.Title,
                    ListedPrice = listing.Price,
                    PublishedQuantity = listing.AvailableQuantity,
                    IsActive = listing.IsActive,
                    LastSyncedAt = now,
                });
                created++;
            }
        }

        var deactivated = 0;
        foreach (var (externalId, row) in existing)
        {
            if (seen.Contains(externalId) || !row.IsActive) continue;
            row.IsActive = false;
            row.LastSyncedAt = now;
            deactivated++;
        }

        await db.SaveChangesAsync(ct);

        // Count after saving so newly-created rows are included. Unmapped
        // listings are the triage queue — an order for one of them still
        // imports, but its line lands without a part.
        var unmapped = await db.ChannelListings
            .CountAsync(l => l.ChannelId == channel.Id && l.IsActive && l.PartId == null, ct);

        db.LogActivityAt(
            "listings-synced",
            $"Synced listings from {integration.Platform}: {created} new, {updated} updated, " +
            $"{deactivated} deactivated, {unmapped} unmapped",
            ("SalesChannel", channel.Id));
        await db.SaveChangesAsync(ct);

        if (unmapped > 0)
        {
            logger.LogInformation(
                "[CHANNEL-LISTINGS] Channel {Channel} has {Unmapped} active listing(s) with no part mapping",
                channel.Code, unmapped);
        }

        return new SyncChannelListingsResult(created, updated, deactivated, unmapped);
    }
}
