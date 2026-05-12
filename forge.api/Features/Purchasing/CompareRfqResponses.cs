using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record CompareRfqResponsesQuery(int RfqId) : IRequest<List<RfqVendorResponseModel>>;

public class CompareRfqResponsesHandler(AppDbContext db)
    : IRequestHandler<CompareRfqResponsesQuery, List<RfqVendorResponseModel>>
{
    public async Task<List<RfqVendorResponseModel>> Handle(CompareRfqResponsesQuery request, CancellationToken cancellationToken)
    {
        var rfq = await db.RequestForQuotes
            .AsNoTracking()
            .Include(r => r.VendorResponses)
                .ThenInclude(v => v.Vendor)
            .FirstOrDefaultAsync(r => r.Id == request.RfqId, cancellationToken)
            ?? throw new KeyNotFoundException($"RFQ {request.RfqId} not found");

        return rfq.VendorResponses
            .OrderBy(v => v.UnitPrice ?? decimal.MaxValue)
            .Select(v => new RfqVendorResponseModel(
                v.Id,
                v.RfqId,
                v.VendorId,
                v.Vendor.CompanyName,
                v.ResponseStatus.ToString(),
                v.UnitPrice,
                v.LeadTimeDays,
                v.MinimumOrderQuantity,
                v.ToolingCost,
                v.QuoteValidUntil,
                v.Notes,
                v.InvitedAt,
                v.RespondedAt,
                v.IsAwarded,
                v.DeclineReason))
            .ToList();
    }
}
