using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record GetChannelSettlementsQuery(ChannelSettlementListQuery Query)
    : IRequest<PagedResponse<ChannelSettlementResponseModel>>;

public class GetChannelSettlementsHandler(AppDbContext db)
    : IRequestHandler<GetChannelSettlementsQuery, PagedResponse<ChannelSettlementResponseModel>>
{
    public async Task<PagedResponse<ChannelSettlementResponseModel>> Handle(
        GetChannelSettlementsQuery request, CancellationToken ct)
    {
        var q = request.Query;
        var query = db.ChannelSettlements.AsNoTracking();

        if (q.ChannelId.HasValue)
            query = query.Where(s => s.ChannelId == q.ChannelId.Value);

        if (q.Status.HasValue)
            query = query.Where(s => s.Status == q.Status.Value);

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(s => EF.Functions.ILike(s.ExternalSettlementId, $"%{term}%"));
        }

        if (q.DateFrom.HasValue)
            query = query.Where(s => s.PeriodEnd >= q.DateFrom.Value);
        if (q.DateTo.HasValue)
            query = query.Where(s => s.PeriodStart <= q.DateTo.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            // Discrepancies first — this list is read to find what needs a human,
            // and a reconciled batch needs no attention at all.
            .OrderBy(s => s.Status == ChannelSettlementStatus.Discrepancy ? 0 : 1)
            .ThenByDescending(s => s.PeriodEnd)
            .Skip(q.Skip)
            .Take(q.EffectivePageSize)
            .Select(s => new ChannelSettlementResponseModel
            {
                Id = s.Id,
                ChannelId = s.ChannelId,
                ChannelName = s.Channel.Name,
                ExternalSettlementId = s.ExternalSettlementId,
                PeriodStart = s.PeriodStart,
                PeriodEnd = s.PeriodEnd,
                DepositedAt = s.DepositedAt,
                ReportedNetAmount = s.ReportedNetAmount,
                // Summed in SQL rather than through the entity's computed
                // property, which would need every line loaded.
                ComputedNetAmount = s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m,
                Variance = s.ReportedNetAmount - (s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m),
                CurrencyCode = s.CurrencyCode,
                Status = s.Status,
                ResolutionNotes = s.ResolutionNotes,
                LineCount = s.Lines.Count,
                UnmatchedLineCount = s.Lines.Count(l => l.ExternalOrderId != null && l.SalesOrderId == null),
                CreatedAt = s.CreatedAt,
            })
            .ToListAsync(ct);

        return new PagedResponse<ChannelSettlementResponseModel>(
            items, totalCount, q.EffectivePage, q.EffectivePageSize);
    }
}
