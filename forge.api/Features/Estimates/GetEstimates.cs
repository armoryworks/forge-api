using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record GetEstimatesQuery(
    int? CustomerId = null,
    QuoteStatus? Status = null) : IRequest<List<EstimateListItemModel>>;

public class GetEstimatesHandler(AppDbContext db) : IRequestHandler<GetEstimatesQuery, List<EstimateListItemModel>>
{
    public async Task<List<EstimateListItemModel>> Handle(GetEstimatesQuery request, CancellationToken ct)
    {
        var query = db.Quotes
            .AsNoTracking()
            .Where(e => e.Type == QuoteType.Estimate)
            .AsQueryable();

        if (request.CustomerId.HasValue)
            query = query.Where(e => e.CustomerId == request.CustomerId.Value);

        if (request.Status.HasValue)
            query = query.Where(e => e.Status == request.Status.Value);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EstimateListItemModel(
                e.Id,
                e.CustomerId,
                e.Customer.Name,
                e.Title ?? string.Empty,
                e.EstimatedAmount ?? 0,
                e.Status.ToString(),
                e.ExpirationDate,
                e.GeneratedQuote != null ? e.GeneratedQuote.Id : (int?)null,
                e.AssignedToId != null
                    ? db.Users.Where(u => u.Id == e.AssignedToId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()
                    : null,
                e.CreatedAt))
            .ToListAsync(ct);
    }
}
