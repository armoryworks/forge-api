namespace Forge.Core.Enums.Accounting;

/// <summary>
/// BANK-001 — match lifecycle of one imported statement line against the GL cash account.
/// Auto-match proposes (<see cref="Suggested"/>) only when exactly ONE candidate journal line
/// fits; a human always confirms. Confirming clears the line in any open reconciliation.
/// </summary>
public enum BankStatementMatchStatus
{
    /// <summary>No single GL candidate found (none, or ambiguous) — needs manual matching.</summary>
    Unmatched,

    /// <summary>Auto-match found exactly one candidate journal line — awaiting human confirmation.</summary>
    Suggested,

    /// <summary>Match confirmed by a user — the journal line is bank-cleared.</summary>
    Confirmed,

    /// <summary>Deliberately excluded (bank fee rows handled by JE, duplicates, noise).</summary>
    Ignored,
}
