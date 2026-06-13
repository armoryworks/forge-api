namespace Forge.Core.Models.Accounting;

/// <summary>One standard-cost variance account's net activity over the period. <see cref="Amount"/> is
/// debit-positive: positive = unfavorable (actual exceeded standard), negative = favorable.</summary>
public sealed record VarianceLineModel(string Key, string Name, decimal Amount)
{
    public bool IsFavorable => Amount < 0m;
}

/// <summary>
/// The standard-cost variance rollup: each of the six variance accounts (+ the production-variance residual)
/// over a date range, plus the lumped <see cref="Total"/> = SUM(lines). One set of postings yields both the
/// decomposed lines and the lumped figure.
/// </summary>
public sealed record VarianceReportModel(
    int BookId, DateOnly From, DateOnly To, IReadOnlyList<VarianceLineModel> Lines, decimal Total);
