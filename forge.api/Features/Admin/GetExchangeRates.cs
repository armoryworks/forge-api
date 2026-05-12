using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetExchangeRatesQuery(
    int? FromCurrencyId = null,
    int? ToCurrencyId = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null) : IRequest<List<ExchangeRateResponseModel>>;

public class GetExchangeRatesHandler(AppDbContext db) : IRequestHandler<GetExchangeRatesQuery, List<ExchangeRateResponseModel>>
{
    public async Task<List<ExchangeRateResponseModel>> Handle(GetExchangeRatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.ExchangeRates
            .AsNoTracking()
            .Include(r => r.FromCurrency)
            .Include(r => r.ToCurrency)
            .AsQueryable();

        if (request.FromCurrencyId.HasValue)
            query = query.Where(r => r.FromCurrencyId == request.FromCurrencyId.Value);

        if (request.ToCurrencyId.HasValue)
            query = query.Where(r => r.ToCurrencyId == request.ToCurrencyId.Value);

        if (request.DateFrom.HasValue)
            query = query.Where(r => r.EffectiveDate >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(r => r.EffectiveDate <= request.DateTo.Value);

        var rates = await query
            .OrderByDescending(r => r.EffectiveDate)
            .ThenBy(r => r.FromCurrency.Code)
            .ToListAsync(cancellationToken);

        return rates.Select(r => new ExchangeRateResponseModel(
            r.Id, r.FromCurrencyId, r.FromCurrency.Code,
            r.ToCurrencyId, r.ToCurrency.Code,
            r.Rate, r.EffectiveDate, r.Source, r.FetchedAt)).ToList();
    }
}
