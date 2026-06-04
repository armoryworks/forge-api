namespace Forge.Core.Enums.Accounting;

/// <summary>
/// When revenue (and the matching COGS) is recognized for a book. Product default
/// is <see cref="PointInTime"/> (control transfer — ship / deliver / complete),
/// which fits manufacturing and one-shot service work. Over-time methods serve
/// long service/project engagements and land in a later phase; see
/// ACCOUNTING_SUITE_PLAN §8.4.
/// <para>Not consumed by the engine until Phase 1 — present now so the method is
/// configuration, not a later migration.</para>
/// </summary>
public enum RevenueRecognitionMethod
{
    /// <summary>Recognize at control transfer (shipment / delivery / completion).</summary>
    PointInTime,

    /// <summary>Recognize over time as the work progresses (percentage-of-completion).</summary>
    PercentOfCompletion,

    /// <summary>Recognize at defined contract milestones.</summary>
    Milestone,
}
