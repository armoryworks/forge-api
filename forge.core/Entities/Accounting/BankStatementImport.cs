using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ BANK-001 — one imported bank statement file (OFX or CSV) staged against a cash GL account.
/// Import is idempotent per transaction: lines dedupe on (cash account, FITID) — re-importing
/// the same file, or an overlapping export, inserts nothing twice. Matching state lives on the
/// lines; this row is the audit header.
/// </summary>
public class BankStatementImport : BaseAuditableEntity
{
    public int BookId { get; set; }

    /// <summary>The cash GL account this statement belongs to (same identity as bank reconciliation).</summary>
    public int CashGlAccountId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public BankStatementFormat Format { get; set; }

    public int ImportedByUserId { get; set; }

    /// <summary>Lines inserted by this import (excludes dedupe-skipped rows).</summary>
    public int LineCount { get; set; }

    /// <summary>Rows skipped because their FITID already existed for this cash account.</summary>
    public int DuplicateCount { get; set; }

    public ICollection<BankStatementLine> Lines { get; set; } = [];
}
