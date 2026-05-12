namespace Forge.Core.Models;

/// <summary>
/// Response for <c>GET /api/v1/system/currency-base</c>. Carries the
/// install's base currency (ISO-4217 code, e.g. "USD") so the UI can
/// decide when to suffix record-level currency codes inline.
/// </summary>
/// <param name="BaseCurrency">ISO-4217 currency code (3 letters).</param>
public record CurrencyBaseResponseModel(string BaseCurrency);
