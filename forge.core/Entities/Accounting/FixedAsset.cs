using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-4 — a depreciable fixed asset. Monthly depreciation posts Dr <see cref="DepreciationExpenseGlAccountId"/>
/// / Cr <see cref="AccumulatedDepreciationGlAccountId"/> for the period amount (straight-line:
/// (Cost − Salvage) / UsefulLifeMonths), capped so accumulated depreciation never exceeds the depreciable base.
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

    public int AssetGlAccountId { get; set; }
    public int AccumulatedDepreciationGlAccountId { get; set; }
    public int DepreciationExpenseGlAccountId { get; set; }

    public ICollection<DepreciationEntry> DepreciationEntries { get; set; } = [];

    /// <summary>Depreciable base = cost less salvage.</summary>
    public decimal DepreciableBase => Cost - SalvageValue;

    /// <summary>Straight-line monthly amount (zero-life guard).</summary>
    public decimal MonthlyStraightLine =>
        UsefulLifeMonths > 0 ? Math.Round(DepreciableBase / UsefulLifeMonths, 2) : 0m;
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
