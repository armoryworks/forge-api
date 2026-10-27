namespace Forge.Api.Features.Accounting;

/// <summary>
/// A job whose WIP has posted to the ledger but whose production cost is not closed yet, with its
/// current GL WIP balance (debit-positive) — one row of the period-close production-cost worklist.
/// <c>IsComplete</c> is false while the job is still open; its cost cannot be closed until it completes.
/// <c>ReceiptVariance</c> is the production variance its receipts already posted (credit-positive); a job with zero
/// WIP is listed when this is not zero, because the close still decomposes it into the named variances.
/// </summary>
public record JobCostCloseCandidateModel(
    int JobId,
    string JobNumber,
    string Title,
    DateTimeOffset? CompletedDate,
    decimal WipBalance,
    bool IsComplete)
{
    public decimal ReceiptVariance { get; init; }
}
