using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

public record GetPortalQuotesQuery(int CustomerId) : IRequest<PortalList<PortalQuoteListItem>>;

public class GetPortalQuotesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetPortalQuotesQuery, PortalList<PortalQuoteListItem>>
{
    public async Task<PortalList<PortalQuoteListItem>> Handle(GetPortalQuotesQuery request, CancellationToken ct)
    {
        var scope = db.Quotes.AsNoTracking()
            .Where(q => q.CustomerId == request.CustomerId && q.Status != QuoteStatus.Superseded
                && q.Status != QuoteStatus.Draft && q.Status != QuoteStatus.ConvertedToQuote);
        var quotes = await scope
            .Include(q => q.Lines)
            .Include(q => q.Currency)
            .OrderByDescending(q => q.CreatedAt)
            .Take(PortalReadRules.ListCap)
            .ToListAsync(ct);
        var totalCount = await PortalReadRules.TotalCountAsync(scope, quotes.Count, ct);
        var validFrom = await PortalReadRules.QuoteValidFromAsync(db, clock, ct);
        var currencyCode = await PortalReadRules.CustomerCurrencyCodeAsync(db, request.CustomerId, ct);

        return new(quotes.Select(q => new PortalQuoteListItem(
            Id: q.Id,
            QuoteNumber: q.QuoteNumber ?? $"#{q.Id}",
            QuoteType: q.Type.ToString(),
            Status: (q.Status == QuoteStatus.Sent && PortalReadRules.IsExpired(q.ExpirationDate, validFrom)
                ? QuoteStatus.Expired
                : q.Status).ToString(),
            QuoteDate: q.SentDate ?? q.CreatedAt,
            ExpiresAt: q.ExpirationDate,
            Total: q.EstimatedAmount ?? q.Total,
            CurrencyCode: q.Currency?.Code ?? currencyCode)).ToList(), totalCount);
    }
}
