using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdateCurrencyCommand(int Id, UpdateCurrencyRequestModel Request) : IRequest;

public class UpdateCurrencyHandler(AppDbContext db) : IRequestHandler<UpdateCurrencyCommand>
{
    public async Task Handle(UpdateCurrencyCommand command, CancellationToken cancellationToken)
    {
        var currency = await db.Currencies.FindAsync(new object[] { command.Id }, cancellationToken)
            ?? throw new KeyNotFoundException($"Currency {command.Id} not found");

        var request = command.Request;

        if (request.IsBaseCurrency && !currency.IsBaseCurrency)
        {
            var existingBase = await db.Currencies
                .Where(c => c.IsBaseCurrency && c.Id != command.Id)
                .ToListAsync(cancellationToken);

            foreach (var c in existingBase)
                c.IsBaseCurrency = false;
        }

        currency.Code = request.Code;
        currency.Name = request.Name;
        currency.Symbol = request.Symbol;
        currency.DecimalPlaces = request.DecimalPlaces;
        currency.IsBaseCurrency = request.IsBaseCurrency;
        currency.IsActive = request.IsActive;
        currency.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(cancellationToken);
    }
}
