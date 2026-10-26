namespace Forge.Api.Services.Fx;

/// <summary>
/// A rate found for a currency pair on a date. <see cref="EffectiveDate"/> is the date of the rate row the
/// lookup used (the older of the two legs when triangulated); null when the pair is the same currency.
/// <see cref="Source"/> is one of <c>same-currency</c>, <c>direct</c>, <c>inverse</c>, <c>triangulated</c>.
/// </summary>
public sealed record ExchangeRateQuote(decimal Rate, DateOnly? EffectiveDate, string Source);
