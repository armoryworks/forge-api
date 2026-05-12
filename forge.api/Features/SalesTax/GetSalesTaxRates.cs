using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesTax;

public record GetSalesTaxRatesQuery : IRequest<List<SalesTaxRateResponseModel>>;

public class GetSalesTaxRatesHandler(AppDbContext db) : IRequestHandler<GetSalesTaxRatesQuery, List<SalesTaxRateResponseModel>>
{
    public async Task<List<SalesTaxRateResponseModel>> Handle(GetSalesTaxRatesQuery request, CancellationToken cancellationToken)
    {
        return await db.SalesTaxRates
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new SalesTaxRateResponseModel(
                r.Id, r.Name, r.Code, r.StateCode, r.Rate,
                r.EffectiveFrom, r.EffectiveTo, r.IsDefault, r.IsActive,
                r.Description, r.ExemptFlag, r.GlPostingAccount))
            .ToListAsync(cancellationToken);
    }
}
