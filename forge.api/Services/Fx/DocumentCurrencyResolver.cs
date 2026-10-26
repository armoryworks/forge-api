using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Services.Fx;

/// <summary>
/// Resolves the currency and booking rate of a document from the request, the party and the rate table,
/// and looks rates up by date. Rates come only from the manual <c>exchange_rates</c> table: the latest row
/// on or before the date for the pair, else the inverse of the reverse pair, else triangulated through the
/// functional currency. Built from the request's <see cref="AppDbContext"/> so handlers can construct it
/// when DI did not supply one.
/// </summary>
public sealed class DocumentCurrencyResolver(AppDbContext db)
{
    public const string CurrencyInactiveCode = "currency-inactive";
    public const string PaymentCurrencyMismatchCode = "payment-currency-mismatch";
    public const string BillPoCurrencyMismatchCode = "bill-po-currency-mismatch";
    public const string InvoiceSoCurrencyMismatchCode = "invoice-so-currency-mismatch";

    private const int RateDecimals = 8;

    private int? _functionalCurrencyId;

    /// <summary>
    /// The functional currency: the first active book's, else the currency flagged as base, else id 1
    /// (the seeded default) on installs that never set either.
    /// </summary>
    public async Task<int> GetFunctionalCurrencyIdAsync(CancellationToken ct)
    {
        if (_functionalCurrencyId is int cached)
            return cached;

        var bookCurrency = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => (int?)b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct);
        var id = bookCurrency is int b and > 0
            ? b
            : await db.Currencies.AsNoTracking()
                .Where(c => c.IsBaseCurrency)
                .OrderBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct) ?? 1;

        _functionalCurrencyId = id;
        return id;
    }

    /// <summary>A null document or party currency means the functional currency.</summary>
    public async Task<int> EffectiveCurrencyIdAsync(int? currencyId, CancellationToken ct)
        => currencyId is int id and > 0 ? id : await GetFunctionalCurrencyIdAsync(ct);

    /// <summary>
    /// The currency and rate to store on a document. <paramref name="requestedCurrencyId"/> wins, then
    /// <paramref name="inheritedCurrencyId"/> (the party or source document), then the functional currency.
    /// A functional document always books at 1. A foreign document books at <paramref name="requestedFxRate"/>
    /// when the caller sent one, else at the rate on file for <paramref name="documentDate"/>.
    /// </summary>
    /// <exception cref="ValidationException">The currency is unknown or inactive (<c>currency-inactive</c>).</exception>
    /// <exception cref="FxRateMissingException">A foreign document has no rate and none is on file.</exception>
    public async Task<DocumentCurrency> ResolveAsync(
        int? requestedCurrencyId,
        decimal? requestedFxRate,
        int? inheritedCurrencyId,
        DateTimeOffset documentDate,
        CancellationToken ct)
    {
        var currency = await ResolveCurrencyAsync(requestedCurrencyId, inheritedCurrencyId, ct);
        if (currency.IsFunctional)
            return currency;

        var rate = requestedFxRate is decimal explicitRate and > 0m
            ? explicitRate
            : (await GetRateAsync(currency.CurrencyId, await GetFunctionalCurrencyIdAsync(ct), DateOnly.FromDateTime(documentDate.UtcDateTime), ct)).Rate;
        return currency with { FxRate = rate };
    }

    /// <summary>
    /// The currency a document takes (requested, else inherited, else functional), checked to exist and be
    /// active. The returned rate is 1; callers that need the booking rate use <see cref="ResolveAsync"/>.
    /// </summary>
    /// <exception cref="ValidationException">The currency is unknown or inactive (<c>currency-inactive</c>).</exception>
    public async Task<DocumentCurrency> ResolveCurrencyAsync(int? requestedCurrencyId, int? inheritedCurrencyId, CancellationToken ct)
    {
        var functionalId = await GetFunctionalCurrencyIdAsync(ct);
        var currencyId = Positive(requestedCurrencyId) ?? Positive(inheritedCurrencyId) ?? functionalId;

        var currency = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == currencyId)
            .Select(c => new { c.Code, c.IsActive })
            .FirstOrDefaultAsync(ct);

        if (currency is null && currencyId == functionalId)
            return new DocumentCurrency(currencyId, 1m, true, null);

        if (currency is null || (!currency.IsActive && currencyId != functionalId))
            throw Failure("CurrencyId", CurrencyInactiveCode,
                currency is null
                    ? $"Currency {currencyId} does not exist."
                    : $"Currency {currency.Code} is inactive. Choose an active currency.");

        return new DocumentCurrency(currencyId, 1m, currencyId == functionalId, currency.Code);
    }

    /// <summary>
    /// The currency and rate a document edit asks for, or null when it changes neither. A new currency without a
    /// rate takes the rate on file for <paramref name="documentDate"/>; a new rate alone keeps the currency.
    /// </summary>
    public async Task<DocumentCurrency?> ResolveChangeAsync(
        int? currentCurrencyId,
        decimal currentFxRate,
        int? requestedCurrencyId,
        decimal? requestedFxRate,
        DateTimeOffset documentDate,
        CancellationToken ct)
    {
        if (requestedCurrencyId is null && requestedFxRate is null)
            return null;

        var current = await EffectiveCurrencyIdAsync(currentCurrencyId, ct);
        var currencyChanged = requestedCurrencyId is int requested && requested != current;
        var resolved = await ResolveAsync(
            requestedCurrencyId ?? current,
            requestedFxRate ?? (currencyChanged ? null : currentFxRate),
            null, documentDate, ct);
        return resolved.CurrencyId == currentCurrencyId && resolved.FxRate == currentFxRate ? null : resolved;
    }

    /// <summary>The id of the currency with ISO code <paramref name="code"/>, or null when none matches.</summary>
    public async Task<int?> CurrencyIdForCodeAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Currencies.AsNoTracking()
            .Where(c => c.Code.ToUpper() == normalized)
            .OrderBy(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The ISO code of a currency, or null when the id is unknown.</summary>
    public Task<string?> CodeForAsync(int currencyId, CancellationToken ct)
        => db.Currencies.AsNoTracking()
            .Where(c => c.Id == currencyId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);

    /// <summary>The rate from <paramref name="fromCurrencyId"/> to <paramref name="toCurrencyId"/> on <paramref name="date"/>.</summary>
    /// <exception cref="FxRateMissingException">No direct, inverse or triangulated rate exists on or before the date.</exception>
    public async Task<ExchangeRateQuote> GetRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        var quote = await TryGetRateAsync(fromCurrencyId, toCurrencyId, date, ct);
        if (quote is not null)
            return quote;

        var codes = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == fromCurrencyId || c.Id == toCurrencyId)
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        throw new FxRateMissingException(
            codes.GetValueOrDefault(fromCurrencyId, fromCurrencyId.ToString()),
            codes.GetValueOrDefault(toCurrencyId, toCurrencyId.ToString()),
            date);
    }

    /// <summary>As <see cref="GetRateAsync"/>, but null when no rate can be found.</summary>
    public async Task<ExchangeRateQuote?> TryGetRateAsync(int fromCurrencyId, int toCurrencyId, DateOnly date, CancellationToken ct)
    {
        if (fromCurrencyId == toCurrencyId)
            return new ExchangeRateQuote(1m, null, "same-currency");

        var single = await TryDirectOrInverseAsync(fromCurrencyId, toCurrencyId, date, ct);
        if (single is not null)
            return single;

        var functionalId = await GetFunctionalCurrencyIdAsync(ct);
        if (fromCurrencyId == functionalId || toCurrencyId == functionalId)
            return null;

        var toBase = await TryDirectOrInverseAsync(fromCurrencyId, functionalId, date, ct);
        if (toBase is null)
            return null;
        var fromBase = await TryDirectOrInverseAsync(functionalId, toCurrencyId, date, ct);
        if (fromBase is null)
            return null;

        var effective = toBase.EffectiveDate < fromBase.EffectiveDate ? toBase.EffectiveDate : fromBase.EffectiveDate;
        return new ExchangeRateQuote(
            Math.Round(toBase.Rate * fromBase.Rate, RateDecimals, MidpointRounding.AwayFromZero),
            effective,
            "triangulated");
    }

    /// <summary>
    /// Every application of a payment must target documents in the payment's own currency; cross-currency
    /// application is not supported.
    /// </summary>
    /// <exception cref="ValidationException">With code <c>payment-currency-mismatch</c>.</exception>
    public static void EnsureSameCurrency(int paymentCurrencyId, int documentCurrencyId, string documentNumber, string? paymentCode, string? documentCode)
    {
        if (paymentCurrencyId != documentCurrencyId)
            throw Failure("Applications", PaymentCurrencyMismatchCode,
                $"{documentNumber} is in {documentCode ?? documentCurrencyId.ToString()}, but this payment is in {paymentCode ?? paymentCurrencyId.ToString()}. A payment can only be applied to documents in its own currency.");
    }

    private static int? Positive(int? id) => id is > 0 ? id : null;

    /// <summary>A FluentValidation failure carrying an error code the UI maps to a translated message.</summary>
    public static ValidationException Failure(string property, string code, string message)
        => new(message, [new ValidationFailure(property, message) { ErrorCode = code }]);

    private async Task<ExchangeRateQuote?> TryDirectOrInverseAsync(int fromId, int toId, DateOnly date, CancellationToken ct)
    {
        var direct = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.FromCurrencyId == fromId && r.ToCurrencyId == toId && r.EffectiveDate <= date && r.Rate > 0m)
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Rate, r.EffectiveDate })
            .FirstOrDefaultAsync(ct);
        if (direct is not null)
            return new ExchangeRateQuote(direct.Rate, direct.EffectiveDate, "direct");

        var reverse = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.FromCurrencyId == toId && r.ToCurrencyId == fromId && r.EffectiveDate <= date && r.Rate > 0m)
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Rate, r.EffectiveDate })
            .FirstOrDefaultAsync(ct);
        return reverse is null
            ? null
            : new ExchangeRateQuote(
                Math.Round(1m / reverse.Rate, RateDecimals, MidpointRounding.AwayFromZero),
                reverse.EffectiveDate,
                "inverse");
    }
}
