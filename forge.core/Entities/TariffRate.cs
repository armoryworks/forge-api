namespace Forge.Core.Entities;

/// <summary>
/// Tariff / import-duty rate keyed on the (HtsCode, CountryOfOrigin) pair.
/// Bought-parts effort PR3 — feeds the landed-cost duty component.
///
/// <para>SCD Type 2: when a rate changes, set <see cref="EffectiveTo"/>
/// on the existing row and insert a new row with the new <see cref="RatePct"/>
/// and a fresh <see cref="EffectiveFrom"/>. Resolution at landed-cost
/// calc time picks the row whose effective window contains the receipt
/// date.</para>
///
/// <para>Today this table is shipped empty. Admin imports rates manually
/// (USITC / customs broker data) or via a future bulk-import endpoint.
/// When no row matches, <c>ITariffResolver</c> returns 0 so the duty
/// component shows as <c>—</c> on the cost tab (distinct from <c>$0</c>
/// = "duty-free"). The <see cref="Source"/> field is free-text audit:
/// "USITC manual import 2026-05-01" / API source ref.</para>
/// </summary>
public class TariffRate : BaseAuditableEntity
{
    /// <summary>
    /// Harmonized Tariff Schedule code (e.g. "8471.30.0100"). Combined
    /// with <see cref="CountryOfOrigin"/> identifies the rate uniquely
    /// within an effective window. Stored exactly as the broker supplies
    /// (we don't normalize dots) so admin lookups match.
    /// </summary>
    public string HtsCode { get; set; } = string.Empty;

    /// <summary>
    /// ISO-3166 alpha-2 country of origin. Same HTS code yields different
    /// rates by origin (Section 301 China tariffs vs Section 232 EU steel,
    /// etc.) — that's why this is part of the natural key.
    /// </summary>
    public string CountryOfOrigin { get; set; } = string.Empty;

    /// <summary>
    /// Duty rate as a percentage (10.5 = 10.5%). Applied to the line's
    /// extended value at landed-cost calc time.
    /// </summary>
    public decimal RatePct { get; set; }

    /// <summary>Inclusive start of this rate's effective window.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// Exclusive end of this rate's effective window. Null = currently
    /// effective (the latest row for this HTS+country pair).
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>
    /// Free-text source reference for audit. Examples: "USITC manual
    /// import 2026-05-01", "Section 301 list 4A 2025-09-01", "Broker
    /// SmartBorder API 2026-04-15". Helpful when an unusual rate is
    /// disputed and a reviewer asks "where did this come from?".
    /// </summary>
    public string? Source { get; set; }
}
