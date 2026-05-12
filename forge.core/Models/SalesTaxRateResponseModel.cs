namespace Forge.Core.Models;

public record SalesTaxRateResponseModel(
    int Id,
    string Name,
    string Code,
    string? StateCode,
    decimal Rate,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsDefault,
    bool IsActive,
    string? Description,
    // Phase 3 F5 — surface full-record fields on the response so a POST-with-
    // everything can be verified via a GET round-trip.
    bool ExemptFlag = false,
    string? GlPostingAccount = null);
