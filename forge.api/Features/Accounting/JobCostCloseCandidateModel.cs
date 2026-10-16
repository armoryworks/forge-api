namespace Forge.Api.Features.Accounting;

/// <summary>
/// A job whose WIP has posted to the ledger but whose production cost is not closed yet, with its
/// current GL WIP balance (debit-positive) — one row of the period-close production-cost worklist.
/// </summary>
public record JobCostCloseCandidateModel(
    int JobId,
    string JobNumber,
    string Title,
    DateTimeOffset? CompletedDate,
    decimal WipBalance);
