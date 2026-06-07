using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — evaluates the pre-close checklist for a book as of a date (GRNI reconciled, AR/AP tie to
/// control). A HardClose is gated on <see cref="CloseChecklistResult.AllPassed"/>.
/// </summary>
public interface IPeriodCloseChecklistService
{
    Task<CloseChecklistResult> EvaluateAsync(int bookId, DateOnly asOf, CancellationToken ct = default);
}
