using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A fiscal year for a book. Dates are <see cref="DateOnly"/> (§5.1).
/// </summary>
public class FiscalYear : BaseEntity
{
    public int BookId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public FiscalYearStatus Status { get; set; } = FiscalYearStatus.Open;

    // ── Close audit (§12) — who/when ran the year-end close. ──
    public int? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public Book Book { get; set; } = null!;
    public ICollection<FiscalPeriod> Periods { get; set; } = [];
}
