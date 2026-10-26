using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>The payment terms list, in picker order. Inactive terms are left out unless asked for.</summary>
public record GetPaymentTermsQuery(bool IncludeInactive = false) : IRequest<List<PaymentTermResponseModel>>;

public class GetPaymentTermsHandler(AppDbContext db) : IRequestHandler<GetPaymentTermsQuery, List<PaymentTermResponseModel>>
{
    public async Task<List<PaymentTermResponseModel>> Handle(GetPaymentTermsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.PaymentTerms.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(PaymentTermMapping.ToResponse).ToList();
    }
}
