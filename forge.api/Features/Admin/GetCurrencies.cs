using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetCurrenciesQuery : IRequest<List<CurrencyResponseModel>>;

public class GetCurrenciesHandler(AppDbContext db) : IRequestHandler<GetCurrenciesQuery, List<CurrencyResponseModel>>
{
    public async Task<List<CurrencyResponseModel>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var currencies = await db.Currencies
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Code)
            .ToListAsync(cancellationToken);

        return currencies.Select(c => new CurrencyResponseModel(
            c.Id, c.Code, c.Name, c.Symbol,
            c.DecimalPlaces, c.IsBaseCurrency, c.IsActive,
            c.SortOrder)).ToList();
    }
}
