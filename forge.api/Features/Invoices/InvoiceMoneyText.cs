using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// Formats an invoice amount for customer- and staff-facing text in the invoice's own currency. The <c>:C</c>
/// format reads the thread culture, which on a background job or an invariant-globalization container prints
/// the generic currency sign instead of the currency.
/// </summary>
public static class InvoiceMoneyText
{
    /// <summary>Loads the invoice currency without making it a required join of the invoice query.</summary>
    public static Task<Currency?> CurrencyAsync(AppDbContext db, int currencyId, CancellationToken ct)
        => db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == currencyId, ct);

    public static string Format(decimal amount, Currency? currency)
    {
        var digits = currency?.DecimalPlaces ?? 2;
        var number = amount.ToString($"N{digits}", CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(currency?.Symbol))
            return $"{currency.Symbol}{number}";
        return string.IsNullOrEmpty(currency?.Code) ? number : $"{number} {currency.Code}";
    }
}
