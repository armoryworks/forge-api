using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record GetRfqsQuery(RfqStatus? Status, string? Search) : IRequest<List<RfqResponseModel>>;

public class GetRfqsHandler(AppDbContext db)
    : IRequestHandler<GetRfqsQuery, List<RfqResponseModel>>
{
    public async Task<List<RfqResponseModel>> Handle(GetRfqsQuery request, CancellationToken cancellationToken)
    {
        var query = db.RequestForQuotes
            .AsNoTracking()
            .Include(r => r.Part)
            .Include(r => r.VendorResponses)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(r =>
                r.RfqNumber.ToLower().Contains(term) ||
                r.Part.PartNumber.ToLower().Contains(term) ||
                (r.Part.Description != null && r.Part.Description.ToLower().Contains(term)) ||
                (r.Description != null && r.Description.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RfqResponseModel(
                r.Id,
                r.RfqNumber,
                r.PartId,
                r.Part.PartNumber,
                r.Part.Description ?? r.Part.Name,
                r.Quantity,
                r.RequiredDate,
                r.Status.ToString(),
                r.Description,
                r.SpecialInstructions,
                r.ResponseDeadline,
                r.SentAt,
                r.AwardedAt,
                r.AwardedVendorResponseId,
                r.GeneratedPurchaseOrderId,
                r.Notes,
                r.VendorResponses.Count,
                r.VendorResponses.Count(v => v.ResponseStatus == RfqResponseStatus.Received),
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
