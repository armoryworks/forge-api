using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-4 — a depreciable fixed asset. Monthly depreciation posts Dr <see cref="DepreciationExpenseGlAccountId"/>
/// / Cr <see cref="AccumulatedDepreciationGlAccountId"/> for the period amount (straight-line:
/// (Cost − Salvage) / UsefulLifeMonths; declining-balance: see <see cref="DecliningBalanceCharge"/>), capped so
/// accumulated depreciation never exceeds the depreciable base. Disposal sets <see cref="Status"/> to Disposed and
/// posts the gain/loss journal (SourceType <c>FixedAssetDisposal</c>, SourceId = the asset id).
/// </summary>
public class FixedAsset : BaseAuditableEntity
{
    public int BookId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? AssetTag { get; set; }

    public decimal Cost { get; set; }
    public decimal SalvageValue { get; set; }
    public DateOnly InServiceDate { get; set; }
    public int UsefulLifeMonths { get; set; }

    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    public FixedAssetStatus Status { get; set; } = FixedAssetStatus.Active;

    /// <summary>Units-of-production — total expected units over the asset's life (shots for a mold). §10.3.</summary>
    public decimal? UsefulLifeUnits { get; set; }

    /// <summary>
    /// The operational <see cref="Forge.Core.Entities.Asset"/> this fixed asset books, for any method; for
    /// units-of-production it also supplies the unit counter (mold shot count). Customer-owned tooling is never
    /// linked (off the balance sheet).
    /// </summary>
    public int? LinkedAssetId { get; set; }
    public Asset? LinkedAsset { get; set; }

    /// <summary>High-water mark of units already depreciated (shots charged-for to date).</summary>
    public decimal LastDepreciatedUnits { get; set; }

    public int AssetGlAccountId { get; set; }
    public int AccumulatedDepreciationGlAccountId { get; set; }
    public int DepreciationExpenseGlAccountId { get; set; }

    public ICollection<DepreciationEntry> DepreciationEntries { get; set; } = [];

    /// <summary>Depreciable base = cost less salvage.</summary>
    public decimal DepreciableBase => Cost - SalvageValue;

    /// <summary>Straight-line monthly amount (zero-life guard).</summary>
    public decimal MonthlyStraightLine =>
        UsefulLifeMonths > 0 ? Math.Round(DepreciableBase / UsefulLifeMonths, 2) : 0m;

    /// <summary>
    /// Declining-balance charge for the next period: twice the straight-line rate on net book value, or
    /// straight-line on the remaining depreciable base over the remaining life when that is larger, capped so
    /// the book value never drops below salvage.
    /// </summary>
    public decimal DecliningBalanceCharge(decimal accumulated, int periodsPosted)
    {
        var remaining = DepreciableBase - accumulated;
        if (UsefulLifeMonths <= 0 || remaining <= 0m)
            return 0m;

        var doubleDeclining = Math.Round((Cost - accumulated) * 2m / UsefulLifeMonths, 2);
        var straightLine = Math.Round(remaining / Math.Max(1, UsefulLifeMonths - periodsPosted), 2);
        return Math.Min(Math.Max(doubleDeclining, straightLine), remaining);
    }
}

/// <summary>One posted month of depreciation for an asset (idempotency: one per asset per period month).</summary>
public class DepreciationEntry : BaseEntity
{
    public int FixedAssetId { get; set; }

    /// <summary>The first day of the depreciated month (the posting's entry date).</summary>
    public DateOnly PeriodMonth { get; set; }

    public decimal Amount { get; set; }
    public long JournalEntryId { get; set; }

    public FixedAsset FixedAsset { get; set; } = null!;
}
