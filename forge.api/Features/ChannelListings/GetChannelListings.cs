using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ChannelListings;

/// <summary>
/// Listing list, and — filtered to unmapped — the triage queue. An unmapped
/// listing is not an error; its orders still import. It is a piece of setup
/// work whose cost is that those order lines carry no part, so nothing
/// downstream can allocate stock or cost them.
/// </summary>
public record GetChannelListingsQuery(ChannelListingListQuery Query)
    : IRequest<PagedResponse<ChannelListingResponseModel>>;

public class GetChannelListingsHandler(AppDbContext db)
    : IRequestHandler<GetChannelListingsQuery, PagedResponse<ChannelListingResponseModel>>
{
    public async Task<PagedResponse<ChannelListingResponseModel>> Handle(
        GetChannelListingsQuery request, CancellationToken ct)
    {
        var q = request.Query;
        var query = db.ChannelListings.AsNoTracking();

        if (q.ChannelId.HasValue)
            query = query.Where(l => l.ChannelId == q.ChannelId.Value);

        if (!q.IncludeInactive)
            query = query.Where(l => l.IsActive);

        if (q.IsUnmapped.HasValue)
        {
            query = q.IsUnmapped.Value
                ? query.Where(l => l.PartId == null)
                : query.Where(l => l.PartId != null);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(l =>
                (l.ExternalSku != null && EF.Functions.ILike(l.ExternalSku, $"%{term}%"))
                || (l.Title != null && EF.Functions.ILike(l.Title, $"%{term}%"))
                || EF.Functions.ILike(l.ExternalListingId, $"%{term}%"));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            // Unmapped first — this list exists mostly to work that queue down.
            .OrderBy(l => l.PartId != null)
            .ThenBy(l => l.ExternalSku)
            .Skip(q.Skip)
            .Take(q.EffectivePageSize)
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
            .ToListAsync(ct);

        return new PagedResponse<ChannelListingResponseModel>(
            items, totalCount, q.EffectivePage, q.EffectivePageSize);
    }
}
