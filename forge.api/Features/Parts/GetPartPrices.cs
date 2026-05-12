using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Returns every PartPrice row for a part — current open row first, then
/// closed history rows in EffectiveFrom DESC order. Powers the price-history
/// table on the part-pricing cluster.
/// </summary>
public record GetPartPricesQuery(int PartId) : IRequest<List<PartPriceResponseModel>>;

public class GetPartPricesHandler(AppDbContext db)
    : IRequestHandler<GetPartPricesQuery, List<PartPriceResponseModel>>
{
    public async Task<List<PartPriceResponseModel>> Handle(
        GetPartPricesQuery request, CancellationToken ct)
    {
        var prices = await db.PartPrices
            .AsNoTracking()
            .Where(p => p.PartId == request.PartId)
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);

        return prices.Select(p => new PartPriceResponseModel(
            p.Id,
            p.PartId,
            p.UnitPrice,
            p.Currency,
            p.EffectiveFrom,
            p.EffectiveTo,
            p.Notes,
            p.CreatedAt)).ToList();
    }
}
