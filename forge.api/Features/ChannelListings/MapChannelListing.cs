using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.ChannelListings;

/// <summary>
/// Points a listing at the part it fulfils from — the operator action that
/// clears an entry off the triage queue.
///
/// <para>Mapping is retroactive by design: order lines already imported against
/// this listing with a null part are back-filled. Without that, mapping a
/// listing would only help future orders and every order that arrived before
/// setup was finished would stay unallocatable forever, which is exactly the
/// backlog an operator is trying to clear.</para>
/// </summary>
public record MapChannelListingCommand(int ListingId, int? PartId) : IRequest<MapChannelListingResult>;

public record MapChannelListingResult(ChannelListingResponseModel Listing, int BackfilledOrderLines);

public class MapChannelListingHandler(AppDbContext db)
    : IRequestHandler<MapChannelListingCommand, MapChannelListingResult>
{
    public async Task<MapChannelListingResult> Handle(MapChannelListingCommand request, CancellationToken ct)
    {
        var listing = await db.ChannelListings
            .Include(l => l.Channel)
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, ct)
            ?? throw new KeyNotFoundException($"ChannelListing {request.ListingId} not found");

        var previousPartId = listing.PartId;

        if (request.PartId is int partId)
        {
            var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId, ct)
                ?? throw new KeyNotFoundException($"Part {partId} not found");

            listing.PartId = partId;

            db.LogActivityAt(
                "listing-mapped",
                $"Listing {listing.ExternalSku ?? listing.ExternalListingId} on {listing.Channel.Name} "
                    + $"mapped to part {part.PartNumber}",
                ("SalesChannel", listing.ChannelId),
                ("Part", partId));
        }
        else
        {
            listing.PartId = null;

            db.LogActivityAt(
                "listing-unmapped",
                $"Listing {listing.ExternalSku ?? listing.ExternalListingId} on {listing.Channel.Name} unmapped",
                ("SalesChannel", listing.ChannelId));
        }

        var backfilled = 0;

        // Only back-fill when a mapping is being SET, and only onto lines that
        // have no part yet. Never overwrite a part someone chose by hand, and
        // never strip parts off historical lines when a mapping is cleared —
        // those orders shipped against a real part and the record must stand.
        if (request.PartId is int newPartId && previousPartId != newPartId && !string.IsNullOrWhiteSpace(listing.ExternalSku))
        {
            var skuToken = $"SKU {listing.ExternalSku}";

            backfilled = await db.SalesOrderLines
                .Where(l => l.PartId == null
                    && l.SalesOrder.ChannelId == listing.ChannelId
                    && l.Notes != null
                    && l.Notes.StartsWith(skuToken))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.PartId, newPartId), ct);
        }

        await db.SaveChangesAsync(ct);

        var model = await db.ChannelListings
            .AsNoTracking()
            .Where(l => l.Id == listing.Id)
            .Select(l => new ChannelListingResponseModel
            {
                Id = l.Id,
                ChannelId = l.ChannelId,
                ChannelName = l.Channel.Name,
                ExternalListingId = l.ExternalListingId,
                ExternalSku = l.ExternalSku,
                Title = l.Title,
                PartId = l.PartId,
                PartNumber = l.Part == null ? null : l.Part.PartNumber,
                PartName = l.Part == null ? null : l.Part.Name,
                ListedPrice = l.ListedPrice,
                PublishedQuantity = l.PublishedQuantity,
                LastSyncedAt = l.LastSyncedAt,
                IsActive = l.IsActive,
                IsUnmapped = l.PartId == null,
            })
            .FirstAsync(ct);

        return new MapChannelListingResult(model, backfilled);
    }
}
