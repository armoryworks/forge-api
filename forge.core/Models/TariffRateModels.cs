namespace Forge.Core.Models;

/// <summary>
/// Bought-parts effort PR4 — admin-side TariffRate models. Tariffs are
/// SCD-2 keyed on (HtsCode, CountryOfOrigin) with effective windows;
/// admins import and supersede rates as broker data changes. The
/// <see cref="Forge.Core.Interfaces.ITariffResolver"/> reads the
/// table at landed-cost calc time.
/// </summary>
public record TariffRateResponseModel(
    int Id,
    string HtsCode,
    string CountryOfOrigin,
    decimal RatePct,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateTariffRateRequestModel(
    string HtsCode,
    string CountryOfOrigin,
    decimal RatePct,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? Source = null);

public record UpdateTariffRateRequestModel(
    decimal RatePct,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Source);
