namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves the effective tariff / import-duty rate for a (HtsCode,
/// CountryOfOrigin) pair as of a given receipt date. Bought-parts
/// effort PR3 — feeds the landed-cost duty component.
///
/// <para>SCD Type 2 lookup: pick the row whose effective window contains
/// the receipt date. When no row matches (no entry at all, or out-of-
/// window), returns 0 — the cost tab renders this as <c>—</c>, distinct
/// from a row whose <c>RatePct = 0</c> ("duty-free").</para>
///
/// <para>Today the table ships empty; admins import broker data manually.
/// Adding a real <c>RatePct</c> immediately starts feeding landed cost
/// without a code change.</para>
/// </summary>
public interface ITariffResolver
{
    /// <summary>
    /// Resolve the effective tariff rate (as a percent — 10.5 = 10.5%).
    /// Returns 0 when no matching <c>TariffRate</c> row exists.
    /// </summary>
    /// <param name="htsCode">HTS code on the part / vendor part. May be null.</param>
    /// <param name="countryOfOrigin">ISO-3166 alpha-2 country. May be null.</param>
    /// <param name="receiptDate">Date of the receipt (drives the effective-window pick).</param>
    Task<decimal> ResolveAsync(
        string? htsCode,
        string? countryOfOrigin,
        DateOnly receiptDate,
        CancellationToken ct);
}
