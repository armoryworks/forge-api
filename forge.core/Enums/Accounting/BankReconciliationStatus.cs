namespace Forge.Core.Enums.Accounting;

/// <summary>Lifecycle of a bank reconciliation. A <c>Finalized</c> rec locks its cleared lines (future recs
/// exclude them).</summary>
public enum BankReconciliationStatus
{
    Draft,
    Finalized,
}
