namespace Forge.Core.Models.Accounting;

/// <summary>
/// A fixed asset with its GL accounts and its depreciation schedule: the months already posted, then the
/// projected months until the depreciable base is used up. <paramref name="IsEditable"/> is true until the
/// first depreciation posts.
/// </summary>
public sealed record FixedAssetDetailModel(
    FixedAssetModel Asset,
    int AssetGlAccountId,
    int AccumulatedDepreciationGlAccountId,
    int DepreciationExpenseGlAccountId,
    bool IsEditable,
    IReadOnlyList<DepreciationScheduleRowModel> Schedule);

/// <summary>
/// One month of a fixed asset's depreciation schedule. Posted rows carry their journal entry; projected rows
/// (time-based methods only) show what a run would charge if every month is run in order.
/// </summary>
public sealed record DepreciationScheduleRowModel(
    DateOnly PeriodMonth,
    decimal Amount,
    decimal AccumulatedDepreciation,
    decimal NetBookValue,
    bool Posted,
    long? JournalEntryId);
