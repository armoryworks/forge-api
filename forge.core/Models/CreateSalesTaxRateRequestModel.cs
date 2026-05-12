namespace Forge.Core.Models;

/// <summary>
/// Sales-tax-rate create payload. Phase 3 F5 extends with exemptFlag (local
/// concern) and glPostingAccount (external accounting integration concern,
/// optional). The first 7 positional args preserve the original signature.
/// </summary>
public record CreateSalesTaxRateRequestModel(
    string Name,
    string Code,
    string? StateCode,
    decimal Rate,
    /// <summary>UTC DateTimeOffset when this rate takes effect. Defaults to now if not provided.</summary>
    DateTimeOffset? EffectiveFrom,
    bool IsDefault,
    string? Description,
    // F5 — full-record fields. exemptFlag defaults false; glPostingAccount
    // is optional and populated by the accounting-sync integration when one
    // is configured.
    bool ExemptFlag = false,
    string? GlPostingAccount = null);
