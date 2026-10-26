using MediatR;

using Forge.Api.Services.Fx;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record GetExchangeRateLookupQuery(int FromCurrencyId, int ToCurrencyId, DateOnly Date)
    : IRequest<ExchangeRateLookupResponseModel>;

public class GetExchangeRateLookupHandler(DocumentCurrencyResolver currencies)
    : IRequestHandler<GetExchangeRateLookupQuery, ExchangeRateLookupResponseModel>
{
    public async Task<ExchangeRateLookupResponseModel> Handle(GetExchangeRateLookupQuery request, CancellationToken ct)
    {
        var quote = await currencies.GetRateAsync(request.FromCurrencyId, request.ToCurrencyId, request.Date, ct);
        return new ExchangeRateLookupResponseModel(quote.Rate, quote.EffectiveDate, quote.Source);
    }
}
