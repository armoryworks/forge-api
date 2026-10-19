using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

public record GetPortalQuotesQuery(int CustomerId) : IRequest<List<PortalQuoteListItem>>;

public class GetPortalQuotesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetPortalQuotesQuery, List<PortalQuoteListItem>>
{
    public async Task<List<PortalQuoteListItem>> Handle(GetPortalQuotesQuery request, CancellationToken ct)
    {
        var quotes = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => q.CustomerId == request.CustomerId && q.Status != QuoteStatus.Superseded
                && q.Status != QuoteStatus.Draft)
            .OrderByDescending(q => q.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        var validFrom = await PortalReadRules.QuoteValidFromAsync(db, clock, ct);
        var currencyCode = await PortalReadRules.FunctionalCurrencyCodeAsync(db, ct);

        return quotes.Select(q => new PortalQuoteListItem(
            Id: q.Id,
            QuoteNumber: q.QuoteNumber ?? $"#{q.Id}",
            QuoteType: q.Type.ToString(),
            Status: (q.Status == QuoteStatus.Sent && PortalReadRules.IsExpired(q.ExpirationDate, validFrom)
                ? QuoteStatus.Expired
                : q.Status).ToString(),
            QuoteDate: q.SentDate ?? q.CreatedAt,
            ExpiresAt: q.ExpirationDate,
            Total: q.EstimatedAmount ?? q.Total,
            CurrencyCode: currencyCode)).ToList();
    }
}
