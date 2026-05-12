using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record CreateCurrencyCommand(CreateCurrencyRequestModel Request) : IRequest<CurrencyResponseModel>;

public class CreateCurrencyHandler(AppDbContext db) : IRequestHandler<CreateCurrencyCommand, CurrencyResponseModel>
{
    public async Task<CurrencyResponseModel> Handle(CreateCurrencyCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var existing = await db.Currencies.AnyAsync(c => c.Code == request.Code, cancellationToken);
        if (existing)
            throw new InvalidOperationException($"Currency with code {request.Code} already exists");

        if (request.IsBaseCurrency)
        {
            var existingBase = await db.Currencies
                .Where(c => c.IsBaseCurrency)
                .ToListAsync(cancellationToken);

            foreach (var c in existingBase)
                c.IsBaseCurrency = false;
        }

        var currency = new Currency
        {
            Code = request.Code,
            Name = request.Name,
            Symbol = request.Symbol,
            DecimalPlaces = request.DecimalPlaces,
            IsBaseCurrency = request.IsBaseCurrency,
            SortOrder = request.SortOrder,
        };

        db.Currencies.Add(currency);
        await db.SaveChangesAsync(cancellationToken);

        return new CurrencyResponseModel(
            currency.Id, currency.Code, currency.Name, currency.Symbol,
            currency.DecimalPlaces, currency.IsBaseCurrency, currency.IsActive,
            currency.SortOrder);
    }
}
