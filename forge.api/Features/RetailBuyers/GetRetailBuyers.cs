using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.RetailBuyers;

public record GetRetailBuyersQuery(RetailBuyerListQuery Query)
    : IRequest<PagedResponse<RetailBuyerResponseModel>>;

public class GetRetailBuyersHandler(AppDbContext db)
    : IRequestHandler<GetRetailBuyersQuery, PagedResponse<RetailBuyerResponseModel>>
{
    public async Task<PagedResponse<RetailBuyerResponseModel>> Handle(
        GetRetailBuyersQuery request, CancellationToken ct)
    {
        var q = request.Query;
        var query = db.RetailBuyers.AsNoTracking();

        if (q.ChannelId.HasValue)
            query = query.Where(b => b.ChannelId == q.ChannelId.Value);

        if (q.IsPurged.HasValue)
        {
            query = q.IsPurged.Value
                ? query.Where(b => b.PurgedAt != null)
                : query.Where(b => b.PurgedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(b =>
                EF.Functions.ILike(b.DisplayName, $"%{term}%")
                || (b.ContactEmail != null && EF.Functions.ILike(b.ContactEmail, $"%{term}%"))
                || EF.Functions.ILike(b.ExternalBuyerId, $"%{term}%"));
        }

        if (q.DateFrom.HasValue)
            query = query.Where(b => b.CreatedAt >= q.DateFrom.Value);
        if (q.DateTo.HasValue)
            query = query.Where(b => b.CreatedAt <= q.DateTo.Value);

        var totalCount = await query.CountAsync(ct);

        query = (q.Sort?.ToLowerInvariant(), q.OrderDescending) switch
        {
            ("displayname", false) => query.OrderBy(b => b.DisplayName),
            ("displayname", true) => query.OrderByDescending(b => b.DisplayName),
            ("ordercount", false) => query.OrderBy(b => b.OrderCount),
            ("ordercount", true) => query.OrderByDescending(b => b.OrderCount),
            ("lastorderat", false) => query.OrderBy(b => b.LastOrderAt),
            ("lastorderat", true) => query.OrderByDescending(b => b.LastOrderAt),
            // Default: most recently active first. A retail buyer list is read
            // to answer "who just ordered", not alphabetically.
            _ => query.OrderByDescending(b => b.LastOrderAt).ThenByDescending(b => b.Id),
        };

        var items = await query
            .Skip(q.Skip)
            .Take(q.EffectivePageSize)
            .Select(b => new RetailBuyerResponseModel
            {
                Id = b.Id,
                ChannelId = b.ChannelId,
                ChannelName = b.Channel.Name,
                ExternalBuyerId = b.ExternalBuyerId,
                DisplayName = b.DisplayName,
                ContactEmail = b.ContactEmail,
                Phone = b.Phone,
                MarketingConsent = b.MarketingConsent,
                FirstOrderAt = b.FirstOrderAt,
                LastOrderAt = b.LastOrderAt,
                OrderCount = b.OrderCount,
                PurgeAfter = b.PurgeAfter,
                PurgedAt = b.PurgedAt,
                LifetimeValue = b.SalesOrders
                    .SelectMany(o => o.Lines)
                    .Sum(l => (decimal?)(l.Quantity * l.UnitPrice)) ?? 0m,
            })
            .ToListAsync(ct);

        return new PagedResponse<RetailBuyerResponseModel>(
            items, totalCount, q.EffectivePage, q.EffectivePageSize);
    }
}
