namespace Forge.Api.Features.Accounting;

/// <summary>
/// The overhead pool as of a date, before closing it: actual overhead recorded into the pool, overhead applied
/// (absorbed) by jobs, and the variance a close on that date would post (actual − applied; positive is
/// under-applied). <paramref name="ClosedOnDate"/> is true when the pool has already been closed as of that
/// date; <paramref name="LastClosedOn"/> is the most recent close date of any period.
/// </summary>
public sealed record OverheadPoolStatusModel(
    DateOnly AsOf,
    decimal ActualOverhead,
    decimal AppliedOverhead,
    decimal Variance,
    bool ClosedOnDate,
    DateOnly? LastClosedOn,
    bool IsConfigured);
