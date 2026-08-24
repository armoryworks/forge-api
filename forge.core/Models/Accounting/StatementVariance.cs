namespace Forge.Core.Models.Accounting;

/// <summary>
/// Comparative-period variance math shared by the financial statements
/// (Profit &amp; Loss, Balance Sheet). Centralised so every line and total computes
/// the same way and the divide-by-zero guard lives in one place.
/// <list type="bullet">
///   <item><b>Variance</b> = current − prior (absolute movement).</item>
///   <item><b>Variance %</b> = variance / |prior| × 100 — a signed percentage. The
///   denominator is the <i>absolute</i> prior so the sign tracks the movement
///   (growth positive, decline negative) even when the prior figure is negative.</item>
/// </list>
/// Both return <c>null</c> when <paramref name="prior"/> is <c>null</c> (no
/// comparison in effect); <see cref="Percent"/> additionally returns <c>null</c>
/// when the prior figure is zero (division undefined — the consumer renders "—").
/// </summary>
public static class StatementVariance
{
    /// <summary>Absolute movement current − prior; <c>null</c> when no prior is supplied.</summary>
    public static decimal? Variance(decimal current, decimal? prior)
        => prior is null ? null : current - prior.Value;

    /// <summary>
    /// Signed percentage movement (current − prior) / |prior| × 100. <c>null</c> when
    /// there is no prior, or the prior is exactly zero (guarded divide-by-zero).
    /// </summary>
    public static decimal? Percent(decimal current, decimal? prior)
        => prior is null || prior.Value == 0m
            ? null
            : (current - prior.Value) / Math.Abs(prior.Value) * 100m;
}
