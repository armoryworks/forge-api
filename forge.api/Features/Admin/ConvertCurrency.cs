using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record ConvertCurrencyQuery(ConvertCurrencyRequestModel Request) : IRequest<decimal>;

public class ConvertCurrencyHandler(ICurrencyService currencyService) : IRequestHandler<ConvertCurrencyQuery, decimal>
{
    public async Task<decimal> Handle(ConvertCurrencyQuery query, CancellationToken cancellationToken)
    {
        return await currencyService.ConvertAsync(
            query.Request.Amount,
            query.Request.FromCurrencyId,
            query.Request.ToCurrencyId,
            query.Request.Date,
            cancellationToken);
    }
}
