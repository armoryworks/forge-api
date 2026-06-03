namespace Forge.Core.Entities.Accounting;

/// <summary>
/// Incremental read-model balance at grain
/// <c>(BookId, GlAccountId, FiscalPeriodId, CurrencyId)</c>, maintained inside
/// the posting transaction (a reversal adjusts it like any other posting — no
/// special case). A rebuild/verify job recomputes from raw <c>JournalLine</c>s
/// and reconciles against these materialized values (drift = bug → alert);
/// statements read this so they don't sum raw lines at scale (§5.1, §5.3).
/// </summary>
public class LedgerBalance : BaseEntity
{
    public int BookId { get; set; }
    public int GlAccountId { get; set; }
    public int FiscalPeriodId { get; set; }
    public int CurrencyId { get; set; }

    /// <summary>Sum of debits posted to this grain (functional amounts).</summary>
    public decimal DebitTotal { get; set; }

    /// <summary>Sum of credits posted to this grain (functional amounts).</summary>
    public decimal CreditTotal { get; set; }

    public Book Book { get; set; } = null!;
    public GlAccount GlAccount { get; set; } = null!;
    public FiscalPeriod FiscalPeriod { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
}
