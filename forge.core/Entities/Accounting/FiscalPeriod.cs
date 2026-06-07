using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A period (1..12/13) within a <see cref="FiscalYear"/>. <c>BookId</c> derives
/// via <see cref="FiscalYearId"/> — not duplicated (§5.1). Implements
/// <see cref="IConcurrencyVersioned"/>: the <c>Version</c> token guards
/// close-vs-post races (the close path takes a row lock and verifies no
/// in-flight entries; §5.1, §9).
/// </summary>
public class FiscalPeriod : BaseEntity, IConcurrencyVersioned
{
    public int FiscalYearId { get; set; }

    /// <summary>1..12 (or 13 for a closing/adjustment period).</summary>
    public int PeriodNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public FiscalPeriodStatus Status { get; set; } = FiscalPeriodStatus.Open;

    /// <summary>Optimistic-locking token guarding close-vs-post races.</summary>
    public uint Version { get; set; }

    // ── Close-transition audit (§12) — who/when last closed or reopened this period. ──
    public int? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int? ReopenedByUserId { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }

    public FiscalYear FiscalYear { get; set; } = null!;
    public ICollection<JournalEntry> JournalEntries { get; set; } = [];
}
