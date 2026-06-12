using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ BANK-001 — one staged bank statement transaction. <see cref="Amount"/> is SIGNED from the
/// bank's perspective on our account: positive = money in (matches a cash DEBIT journal line),
/// negative = money out (matches a cash CREDIT). <see cref="Fitid"/> is the dedupe key — the
/// OFX FITID verbatim, or a content hash for CSV rows (unique per cash account).
/// </summary>
public class BankStatementLine : BaseEntity
{
    public int BankStatementImportId { get; set; }

    /// <summary>Denormalized from the import — scopes the FITID dedupe and the match queries.</summary>
    public int CashGlAccountId { get; set; }

    public DateOnly PostedDate { get; set; }

    /// <summary>Signed: + deposit / − withdrawal.</summary>
    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>OFX FITID, or a content hash for CSV. Unique per (CashGlAccountId, Fitid).</summary>
    public string Fitid { get; set; } = string.Empty;

    public BankStatementMatchStatus MatchStatus { get; set; } = BankStatementMatchStatus.Unmatched;

    /// <summary>The matched cash journal line (set when Suggested or Confirmed).</summary>
    public long? MatchedJournalLineId { get; set; }

    public int? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    public BankStatementImport Import { get; set; } = null!;
    public JournalLine? MatchedJournalLine { get; set; }
}
