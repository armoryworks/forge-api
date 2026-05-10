using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.CustomerPortal;

public record GetPortalQuotesQuery(int CustomerId) : IRequest<List<PortalQuoteListItem>>;

public class GetPortalQuotesHandler(AppDbContext db)
    : IRequestHandler<GetPortalQuotesQuery, List<PortalQuoteListItem>>
{
    public async Task<List<PortalQuoteListItem>> Handle(GetPortalQuotesQuery request, CancellationToken ct)
    {
        var quotes = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => q.CustomerId == request.CustomerId)
            .OrderByDescending(q => q.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return quotes.Select(q => new PortalQuoteListItem(
            Id: q.Id,
            QuoteNumber: q.QuoteNumber ?? $"#{q.Id}",
            QuoteType: q.Type.ToString(),
            Status: q.Status.ToString(),
            QuoteDate: q.SentDate ?? q.CreatedAt,
            ExpiresAt: q.ExpirationDate,
            Total: q.EstimatedAmount ?? q.Total)).ToList();
    }
}
