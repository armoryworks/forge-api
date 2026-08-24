namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A budget line for a single GL account in a fiscal year — master data behind
/// budget-vs-actual P&amp;L reporting. <see cref="PeriodMonth"/> distinguishes a
/// full-year budget (<c>null</c>) from a monthly one (1..12). Soft-deletable and
/// audited (<see cref="BaseAuditableEntity"/>); one live row per
/// (book, account, year, month) is enforced by
/// <c>ux_acct_budgets_book_account_year_period</c>.
/// </summary>
public class AcctBudget : BaseAuditableEntity
{
    public int BookId { get; set; }

    public int GlAccountId { get; set; }

    public int FiscalYear { get; set; }

    /// <summary><c>null</c> = full-year budget; 1..12 = a specific month.</summary>
    public int? PeriodMonth { get; set; }

    public decimal Amount { get; set; }

    public Book Book { get; set; } = null!;
    public GlAccount GlAccount { get; set; } = null!;
}
