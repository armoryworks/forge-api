namespace Forge.Api.Services.Fx;

/// <summary>The currency and booking rate a document is stored with, as resolved at create or update.</summary>
public sealed record DocumentCurrency(int CurrencyId, decimal FxRate, bool IsFunctional, string? Code);
