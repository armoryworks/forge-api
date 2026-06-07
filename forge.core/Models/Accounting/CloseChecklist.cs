namespace Forge.Core.Models.Accounting;

/// <summary>One pre-close check (e.g. "GRNI reconciled", "AR ties to control").</summary>
public sealed record CloseChecklistItem(string Key, string Label, bool Passed, string Detail);

/// <summary>
/// ⚡ Phase-3 — the pre-close checklist for a book as of a date. A HardClose is blocked unless
/// <see cref="AllPassed"/>; the UI shows this so an operator can see what's dirty before locking.
/// </summary>
public sealed record CloseChecklistResult(int BookId, DateOnly AsOf, IReadOnlyList<CloseChecklistItem> Items)
{
    public bool AllPassed => Items.All(i => i.Passed);
}
