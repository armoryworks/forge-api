using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Services.Fx;
using Forge.Core.Enums;
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

        if (request.IsBaseCurrency != currency.IsBaseCurrency)
            await EnsureBaseCurrencyChangeableAsync(cancellationToken);

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

        if (request.IsBaseCurrency)
        {
            foreach (var book in await db.Books.Where(b => b.FunctionalCurrencyId != currency.Id).ToListAsync(cancellationToken))
                book.FunctionalCurrencyId = currency.Id;

            var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "currency.base", cancellationToken);
            if (setting is not null && !string.Equals(setting.Value, request.Code, StringComparison.OrdinalIgnoreCase))
                setting.Value = request.Code;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureBaseCurrencyChangeableAsync(CancellationToken ct)
    {
        var booked = await db.JournalEntries.IgnoreQueryFilters().AnyAsync(ct)
            || await db.Invoices.AnyAsync(i => i.Status != InvoiceStatus.Draft, ct)
            || await db.VendorBills.AnyAsync(b => b.Status != VendorBillStatus.Draft, ct)
            || await db.Payments.AnyAsync(ct)
            || await db.VendorPayments.AnyAsync(ct);
        if (booked)
            throw new CurrencyLockedException(
                CurrencyLockedException.BaseLocked,
                "The base currency can't change once journal entries, payments or issued invoices and bills exist.");
    }
}
