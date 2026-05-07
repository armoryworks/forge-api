using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.TariffRates;

public record GetTariffRateByIdQuery(int Id) : IRequest<TariffRateResponseModel>;

public class GetTariffRateByIdHandler(AppDbContext db)
    : IRequestHandler<GetTariffRateByIdQuery, TariffRateResponseModel>
{
    public async Task<TariffRateResponseModel> Handle(GetTariffRateByIdQuery request, CancellationToken ct)
    {
        var t = await db.TariffRates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"TariffRate {request.Id} not found");
        return new TariffRateResponseModel(
            t.Id, t.HtsCode, t.CountryOfOrigin, t.RatePct,
            t.EffectiveFrom, t.EffectiveTo, t.Source, t.CreatedAt, t.UpdatedAt);
    }
}
