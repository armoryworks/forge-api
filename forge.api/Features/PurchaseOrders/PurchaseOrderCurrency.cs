using Forge.Api.Services.Fx;
using Forge.Core.Entities;

namespace Forge.Api.Features.PurchaseOrders;

/// <summary>
/// Keeps a purchase order's <see cref="PurchaseOrder.CurrencyId"/>, its legacy <see cref="PurchaseOrder.QuoteCurrency"/>
/// code and its <see cref="PurchaseOrder.FxRate"/> in step. A PO created before multi-currency has a null
/// CurrencyId and is read through its QuoteCurrency code, else as functional.
/// </summary>
internal static class PurchaseOrderCurrency
{
    /// <summary>The PO's currency: its CurrencyId, else the currency matching its QuoteCurrency code, else functional.</summary>
    public static async Task<int> EffectiveCurrencyIdAsync(
        DocumentCurrencyResolver currencies, PurchaseOrder po, CancellationToken ct)
        => po.CurrencyId
            ?? await currencies.CurrencyIdForCodeAsync(po.QuoteCurrency, ct)
            ?? await currencies.GetFunctionalCurrencyIdAsync(ct);

    /// <summary>
    /// Sets the PO's currency, code and rate. A functional PO locks at 1. A foreign PO takes
    /// <paramref name="fxRate"/> when given, else the rate on file for <paramref name="date"/>; with no rate on
    /// file the rate stays empty unless <paramref name="rateRequired"/>, when the lookup throws
    /// <see cref="FxRateMissingException"/>.
    /// </summary>
    public static async Task ApplyAsync(
        DocumentCurrencyResolver currencies,
        PurchaseOrder po,
        int currencyId,
        decimal? fxRate,
        DateTimeOffset date,
        bool rateRequired,
        CancellationToken ct)
    {
        var currency = await currencies.ResolveCurrencyAsync(currencyId, null, ct);
        po.CurrencyId = currency.CurrencyId;
        if (currency.Code is { Length: 3 } code)
            po.QuoteCurrency = code;

        if (currency.IsFunctional)
        {
            po.FxRate = 1m;
            return;
        }

        if (fxRate is decimal explicitRate and > 0m)
        {
            po.FxRate = explicitRate;
            return;
        }

        var functionalId = await currencies.GetFunctionalCurrencyIdAsync(ct);
        var day = DateOnly.FromDateTime(date.UtcDateTime);
        var quote = rateRequired
            ? await currencies.GetRateAsync(currency.CurrencyId, functionalId, day, ct)
            : await currencies.TryGetRateAsync(currency.CurrencyId, functionalId, day, ct);
        po.FxRate = quote?.Rate;
        if (quote is not null)
            po.FxRateSource = $"rate table {quote.EffectiveDate:yyyy-MM-dd}";
    }

    /// <summary>
    /// The rate that values a receipt against this PO: the PO's locked rate, else the rate on file for the
    /// receipt date. 1 for a functional-currency PO.
    /// </summary>
    public static async Task<decimal> ReceiptRateAsync(
        DocumentCurrencyResolver currencies, PurchaseOrder po, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var currencyId = await EffectiveCurrencyIdAsync(currencies, po, ct);
        var functionalId = await currencies.GetFunctionalCurrencyIdAsync(ct);
        if (currencyId == functionalId)
            return 1m;
        if (po.FxRate is decimal locked and > 0m)
            return locked;
        return (await currencies.GetRateAsync(currencyId, functionalId, DateOnly.FromDateTime(receivedAt.UtcDateTime), ct)).Rate;
    }
}
