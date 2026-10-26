namespace Forge.Core.Models;

/// <summary>
/// The rate the server would book for a currency pair on a date: <c>Rate</c> from <c>from</c> to <c>to</c>,
/// the <c>EffectiveDate</c> of the rate row used, and how it was found (<c>same-currency</c>, <c>direct</c>,
/// <c>inverse</c>, <c>triangulated</c>).
/// </summary>
public record ExchangeRateLookupResponseModel(decimal Rate, DateOnly? EffectiveDate, string Source);
