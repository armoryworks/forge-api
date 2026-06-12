namespace Forge.Core.Models.Accounting;

/// <summary>⚡ BANK-001 — list projection of one statement import with its match-state rollup.</summary>
public record BankStatementImportModel(
    int Id,
    int CashGlAccountId,
    string FileName,
    string Format,
    int LineCount,
    int DuplicateCount,
    int UnmatchedCount,
    int SuggestedCount,
    int ConfirmedCount,
    int IgnoredCount,
    DateTimeOffset CreatedAt);
