using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class DepreciationService(
    AppDbContext db, IPostingEngine postingEngine, ILogger<DepreciationService>? logger = null) : IDepreciationService
{
    public const string KeyGainLossOnDisposal = "GAIN_LOSS_ON_DISPOSAL";
    public const string DisposalSourceType = "FixedAssetDisposal";

    private static readonly AccountType[] ProceedsAccountTypes = [AccountType.Asset];
    private static readonly AccountType[] GainLossAccountTypes = [AccountType.Income, AccountType.Expense];
    private const int MaxProjectedMonths = 1200;

    public async Task<FixedAssetModel> CreateAssetAsync(CreateFixedAssetModel model, CancellationToken ct = default)
    {
        await ValidateAsync(model.UsefulLifeMonths, model.Cost, model.SalvageValue, model.Method,
            model.UsefulLifeUnits, model.LinkedAssetId, ct);

        var asset = new FixedAsset
        {
            BookId = model.BookId,
            Name = model.Name,
            AssetTag = model.AssetTag,
            Cost = model.Cost,
            SalvageValue = model.SalvageValue,
            InServiceDate = model.InServiceDate,
            UsefulLifeMonths = model.UsefulLifeMonths,
            Method = model.Method,
            UsefulLifeUnits = model.Method == DepreciationMethod.UnitsOfProduction ? model.UsefulLifeUnits : null,
            LinkedAssetId = model.LinkedAssetId,
            Status = FixedAssetStatus.Active,
            AssetGlAccountId = model.AssetGlAccountId,
            AccumulatedDepreciationGlAccountId = model.AccumulatedDepreciationGlAccountId,
            DepreciationExpenseGlAccountId = model.DepreciationExpenseGlAccountId,
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        return Map(asset, accumulated: 0m);
    }

    public async Task<IReadOnlyList<FixedAssetModel>> ListAssetsAsync(int bookId, CancellationToken ct = default)
    {
        var assets = await db.FixedAssets
            .AsNoTracking()
            .Where(a => a.BookId == bookId)
            .Include(a => a.DepreciationEntries)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return assets.Select(a => Map(a, a.DepreciationEntries.Sum(d => d.Amount))).ToList();
    }

    public async Task<DepreciationRunResult> RunDepreciationAsync(
        int bookId, DateOnly periodMonth, int postedByUserId, CancellationToken ct = default)
    {
        // Normalize to the first of the month.
        var month = new DateOnly(periodMonth.Year, periodMonth.Month, 1);

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId).Select(b => (int?)b.FunctionalCurrencyId).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {bookId} not found for depreciation.");

        var assets = await EligibleAssets(bookId, month).ToListAsync(ct);

        var count = 0;
        var total = 0m;
        var journalEntryIds = new List<long>();

        foreach (var asset in assets)
        {
            // Idempotent: skip a month already posted for this asset.
            if (asset.DepreciationEntries.Any(d => d.PeriodMonth == month))
                continue;

            var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
            if (asset.DepreciableBase - accumulated <= 0m)
            {
                asset.Status = FixedAssetStatus.FullyDepreciated;
                continue;
            }

            var (amount, unitsThisPeriod) = ChargeFor(asset, month, accumulated);
            if (amount <= 0m)
                continue;

            var entry = await postingEngine.PostAsync(new PostingRequest
            {
                BookId = bookId,
                EntryDate = month,
                Source = JournalSource.Depreciation,
                SourceType = "Depreciation",
                SourceId = asset.Id,
                CurrencyId = currencyId,
                Memo = $"Depreciation — {asset.Name} ({month:yyyy-MM})",
                IdempotencyKey = $"{JournalSource.Depreciation}:Asset:{asset.Id}:{month:yyyyMM}",
                Lines =
                [
                    new PostingLine { GlAccountId = asset.DepreciationExpenseGlAccountId, Debit = amount, Description = "Depreciation expense" },
                    new PostingLine { GlAccountId = asset.AccumulatedDepreciationGlAccountId, Credit = amount, Description = "Accumulated depreciation" },
                ],
            }, postedByUserId, ct);

            asset.DepreciationEntries.Add(new DepreciationEntry
            {
                FixedAssetId = asset.Id, PeriodMonth = month, Amount = amount, JournalEntryId = entry.Id,
            });

            if (unitsThisPeriod > 0m)
                asset.LastDepreciatedUnits += unitsThisPeriod; // high-water mark of shots charged-for

            if (accumulated + amount >= asset.DepreciableBase)
                asset.Status = FixedAssetStatus.FullyDepreciated;

            count++;
            total += amount;
            journalEntryIds.Add(entry.Id);
        }

        await db.SaveChangesAsync(ct);
        return new DepreciationRunResult(bookId, month, count, total, journalEntryIds);
    }

    public async Task<DepreciationPreviewResult> PreviewDepreciationAsync(
        int bookId, DateOnly periodMonth, CancellationToken ct = default)
    {
        var month = new DateOnly(periodMonth.Year, periodMonth.Month, 1);

        if (!await db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, ct))
            throw new KeyNotFoundException($"Book {bookId} not found for depreciation.");

        var assets = await EligibleAssets(bookId, month).AsNoTracking().ToListAsync(ct);

        var lines = new List<DepreciationPreviewLine>();
        foreach (var asset in assets.OrderBy(a => a.Name))
        {
            if (asset.DepreciationEntries.Any(d => d.PeriodMonth == month))
                continue;

            var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
            if (asset.DepreciableBase - accumulated <= 0m)
                continue;

            var (amount, _) = ChargeFor(asset, month, accumulated);
            if (amount <= 0m)
                continue;

            lines.Add(new DepreciationPreviewLine(
                asset.Id, asset.Name, asset.AssetTag, amount, asset.Cost - accumulated - amount));
        }

        return new DepreciationPreviewResult(bookId, month, lines, lines.Sum(l => l.Amount));
    }

    public async Task<FixedAssetDetailModel> GetAssetAsync(int fixedAssetId, CancellationToken ct = default)
    {
        var asset = await db.FixedAssets.AsNoTracking()
            .Include(a => a.DepreciationEntries)
            .FirstOrDefaultAsync(a => a.Id == fixedAssetId, ct)
            ?? throw new KeyNotFoundException($"Fixed asset {fixedAssetId} not found.");

        return Detail(asset);
    }

    public async Task<FixedAssetDetailModel> UpdateAssetAsync(
        int fixedAssetId, UpdateFixedAssetModel model, CancellationToken ct = default)
    {
        var asset = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .FirstOrDefaultAsync(a => a.Id == fixedAssetId, ct)
            ?? throw new KeyNotFoundException($"Fixed asset {fixedAssetId} not found.");

        if (asset.Status != FixedAssetStatus.Active || asset.DepreciationEntries.Count > 0)
            throw new InvalidOperationException(
                $"Fixed asset {asset.Name} can no longer be edited: depreciation has already posted for it. " +
                "Dispose of it and register a new asset to change its cost, life or accounts.");

        await ValidateAsync(model.UsefulLifeMonths, model.Cost, model.SalvageValue, model.Method,
            model.UsefulLifeUnits, model.LinkedAssetId, ct);

        asset.Name = model.Name;
        asset.AssetTag = model.AssetTag;
        asset.Cost = model.Cost;
        asset.SalvageValue = model.SalvageValue;
        asset.InServiceDate = model.InServiceDate;
        asset.UsefulLifeMonths = model.UsefulLifeMonths;
        asset.Method = model.Method;
        asset.UsefulLifeUnits = model.Method == DepreciationMethod.UnitsOfProduction ? model.UsefulLifeUnits : null;
        asset.LinkedAssetId = model.LinkedAssetId;
        asset.AssetGlAccountId = model.AssetGlAccountId;
        asset.AccumulatedDepreciationGlAccountId = model.AccumulatedDepreciationGlAccountId;
        asset.DepreciationExpenseGlAccountId = model.DepreciationExpenseGlAccountId;
        await db.SaveChangesAsync(ct);

        return Detail(asset);
    }

    public async Task<IReadOnlyList<LinkableAssetModel>> ListLinkableAssetsAsync(int bookId, CancellationToken ct = default)
    {
        var linked = await db.FixedAssets.AsNoTracking()
            .Where(f => f.BookId == bookId && f.LinkedAssetId != null && f.Status != FixedAssetStatus.Disposed)
            .Select(f => new { f.Id, LinkedAssetId = f.LinkedAssetId!.Value })
            .ToListAsync(ct);
        var byAsset = linked.GroupBy(f => f.LinkedAssetId).ToDictionary(g => g.Key, g => g.Min(f => f.Id));

        var assets = await db.Assets.AsNoTracking()
            .Where(a => !a.IsCustomerOwned)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.SerialNumber, a.AcquisitionCost, a.CurrentShotCount })
            .ToListAsync(ct);

        return assets
            .Select(a => new LinkableAssetModel(
                a.Id, a.Name, a.SerialNumber, a.AcquisitionCost, a.CurrentShotCount,
                byAsset.TryGetValue(a.Id, out var fixedAssetId) ? fixedAssetId : null))
            .ToList();
    }

    public async Task<FixedAssetDisposalResult> DisposeAssetAsync(
        int fixedAssetId, DisposeFixedAssetModel model, int disposedByUserId, CancellationToken ct = default)
    {
        var asset = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Include(a => a.LinkedAsset)
            .FirstOrDefaultAsync(a => a.Id == fixedAssetId, ct)
            ?? throw new KeyNotFoundException($"Fixed asset {fixedAssetId} not found.");

        if (asset.Status == FixedAssetStatus.Disposed)
            throw new InvalidOperationException($"Fixed asset {asset.Name} has already been disposed.");
        if (model.DisposalDate < asset.InServiceDate)
            throw Invalid(nameof(model.DisposalDate), "The disposal date cannot be before the asset's in-service date.");
        if (model.Proceeds < 0m)
            throw Invalid(nameof(model.Proceeds), "Proceeds cannot be negative.");

        var disposalMonth = new DateOnly(model.DisposalDate.Year, model.DisposalDate.Month, 1);
        var laterMonth = asset.DepreciationEntries
            .Where(d => d.PeriodMonth > disposalMonth)
            .OrderBy(d => d.PeriodMonth)
            .Select(d => (DateOnly?)d.PeriodMonth)
            .FirstOrDefault();
        if (laterMonth is DateOnly posted)
            throw new InvalidOperationException(
                $"Depreciation is already posted for {posted:yyyy-MM}, after the disposal date. Dispose on or after that month.");

        var proceedsAccount = model.Proceeds > 0m
            ? await PostableGlAccountLookup.ResolveAsync(db, asset.BookId, model.ProceedsGlAccountId, null,
                nameof(model.ProceedsGlAccountId), ProceedsAccountTypes, ct)
                ?? throw Invalid(nameof(model.ProceedsGlAccountId), "Choose the account that received the sale proceeds.")
            : null;
        var gainLossAccount = await PostableGlAccountLookup.ResolveAsync(db, asset.BookId, model.GainLossGlAccountId,
            KeyGainLossOnDisposal, nameof(model.GainLossGlAccountId), GainLossAccountTypes, ct);

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == asset.BookId).Select(b => (int?)b.FunctionalCurrencyId).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {asset.BookId} not found for the disposal.");

        var catchUp = await PostCatchUpDepreciationAsync(asset, disposalMonth, model.DisposalDate, currencyId, disposedByUserId, ct);

        var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
        var netBookValue = asset.Cost - accumulated;
        var gainLoss = model.Proceeds - netBookValue;

        if (gainLoss != 0m && gainLossAccount is null)
            throw Invalid(nameof(model.GainLossGlAccountId),
                "Choose the income or expense account for the gain or loss on this disposal.");

        var lines = new List<PostingLine>();
        if (accumulated > 0m)
            lines.Add(new PostingLine { GlAccountId = asset.AccumulatedDepreciationGlAccountId, Debit = accumulated, Description = "Accumulated depreciation written off" });
        if (proceedsAccount is not null)
            lines.Add(new PostingLine { GlAccountId = proceedsAccount.Id, Debit = model.Proceeds, Description = "Disposal proceeds" });
        if (asset.Cost > 0m)
            lines.Add(new PostingLine { GlAccountId = asset.AssetGlAccountId, Credit = asset.Cost, Description = "Asset cost written off" });
        if (gainLoss > 0m)
            lines.Add(new PostingLine { GlAccountId = gainLossAccount!.Id, Credit = gainLoss, Description = "Gain on disposal" });
        else if (gainLoss < 0m)
            lines.Add(new PostingLine { GlAccountId = gainLossAccount!.Id, Debit = -gainLoss, Description = "Loss on disposal" });

        long? journalEntryId = null;
        if (lines.Count > 0)
        {
            var entry = await postingEngine.PostAsync(new PostingRequest
            {
                BookId = asset.BookId,
                EntryDate = model.DisposalDate,
                Source = JournalSource.Depreciation,
                SourceType = DisposalSourceType,
                SourceId = asset.Id,
                CurrencyId = currencyId,
                Memo = string.IsNullOrWhiteSpace(model.Memo) ? $"Disposal — {asset.Name}" : model.Memo.Trim(),
                IdempotencyKey = $"{JournalSource.Depreciation}:Asset:{asset.Id}:DISPOSAL",
                Lines = lines,
            }, disposedByUserId, ct);
            journalEntryId = entry.Id;
        }

        asset.Status = FixedAssetStatus.Disposed;

        var description = $"Disposed of fixed asset {asset.Name} on {model.DisposalDate:MM/dd/yyyy}: proceeds "
            + $"{model.Proceeds:N2}, book value {netBookValue:N2}, {(gainLoss >= 0m ? "gain" : "loss")} {Math.Abs(gainLoss):N2}";
        if (asset.LinkedAssetId is int linkedAssetId)
            db.LogActivityAt("disposed", description, ("FixedAsset", asset.Id), ("Asset", linkedAssetId));
        else
            db.LogActivityAt("disposed", description, ("FixedAsset", asset.Id));

        await db.SaveChangesAsync(ct);

        return new FixedAssetDisposalResult(
            Map(asset, accumulated), journalEntryId, catchUp, netBookValue, model.Proceeds, gainLoss);
    }

    private async Task ValidateAsync(
        int usefulLifeMonths, decimal cost, decimal salvageValue, DepreciationMethod method,
        decimal? usefulLifeUnits, int? linkedAssetId, CancellationToken ct)
    {
        if (usefulLifeMonths <= 0)
            throw new InvalidOperationException("Useful life must be a positive number of months.");
        if (cost < salvageValue)
            throw new InvalidOperationException("Salvage value cannot exceed cost.");

        if (method == DepreciationMethod.UnitsOfProduction)
        {
            if (usefulLifeUnits is not > 0)
                throw new InvalidOperationException(
                    "Units-of-production depreciation requires a positive useful life in units (total expected shots).");
            if (linkedAssetId is null)
                throw new InvalidOperationException(
                    "Units-of-production depreciation requires a linked operational asset supplying the shot count.");
        }

        if (linkedAssetId is int linkedId)
        {
            var linked = await db.Assets.AsNoTracking()
                .Where(a => a.Id == linkedId)
                .Select(a => new { a.IsCustomerOwned })
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Linked operational asset {linkedId} not found.");

            if (linked.IsCustomerOwned)
                throw new InvalidOperationException(
                    "Customer-owned tooling cannot be capitalized — it stays off the balance sheet and is " +
                    "memo-tracked operationally only. Link a company-owned asset instead.");
        }
    }

    private async Task<decimal> PostCatchUpDepreciationAsync(
        FixedAsset asset, DateOnly disposalMonth, DateOnly entryDate, int currencyId, int userId, CancellationToken ct)
    {
        var (amount, periodMonth, units) = CatchUpCharge(asset, disposalMonth);
        if (amount <= 0m || periodMonth is not DateOnly month)
            return 0m;

        var entry = await postingEngine.PostAsync(new PostingRequest
        {
            BookId = asset.BookId,
            EntryDate = entryDate,
            Source = JournalSource.Depreciation,
            SourceType = "Depreciation",
            SourceId = asset.Id,
            CurrencyId = currencyId,
            Memo = $"Depreciation catch-up to disposal — {asset.Name}",
            IdempotencyKey = $"{JournalSource.Depreciation}:Asset:{asset.Id}:CATCHUP",
            Lines =
            [
                new PostingLine { GlAccountId = asset.DepreciationExpenseGlAccountId, Debit = amount, Description = "Depreciation expense" },
                new PostingLine { GlAccountId = asset.AccumulatedDepreciationGlAccountId, Credit = amount, Description = "Accumulated depreciation" },
            ],
        }, userId, ct);

        asset.DepreciationEntries.Add(new DepreciationEntry
        {
            FixedAssetId = asset.Id, PeriodMonth = month, Amount = amount, JournalEntryId = entry.Id,
        });
        asset.LastDepreciatedUnits += units;
        return amount;
    }

    private static (decimal Amount, DateOnly? PeriodMonth, decimal Units) CatchUpCharge(FixedAsset asset, DateOnly disposalMonth)
    {
        var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
        var remaining = asset.DepreciableBase - accumulated;
        if (remaining <= 0m)
            return (0m, null, 0m);

        var postedMonths = asset.DepreciationEntries.Select(d => d.PeriodMonth).ToHashSet();

        if (asset.Method == DepreciationMethod.UnitsOfProduction)
        {
            if (postedMonths.Contains(disposalMonth) || asset.LinkedAsset is null
                || asset.LinkedAsset.DeletedAt is not null || asset.UsefulLifeUnits is not > 0)
                return (0m, null, 0m);

            var units = Math.Max(0m, asset.LinkedAsset.CurrentShotCount - asset.LastDepreciatedUnits);
            var amount = Math.Min(Math.Round(asset.DepreciableBase * units / asset.UsefulLifeUnits.Value, 2), remaining);
            return amount > 0m ? (amount, disposalMonth, units) : (0m, null, 0m);
        }

        var amounts = asset.DepreciationEntries.ToDictionary(d => d.PeriodMonth, d => d.Amount);
        var running = 0m;
        var catchUp = 0m;
        var periods = 0;
        DateOnly? lastMonth = null;
        for (var month = new DateOnly(asset.InServiceDate.Year, asset.InServiceDate.Month, 1);
             month < disposalMonth;
             month = month.AddMonths(1))
        {
            if (amounts.TryGetValue(month, out var posted))
            {
                running += posted;
                periods++;
                continue;
            }

            var charge = TimeBasedCharge(asset, running, periods);
            periods++;
            if (charge <= 0m)
                continue;

            running += charge;
            catchUp += charge;
            lastMonth = month;
        }

        catchUp = Math.Min(catchUp, remaining);
        return catchUp > 0m ? (catchUp, lastMonth, 0m) : (0m, null, 0m);
    }

    private IQueryable<FixedAsset> EligibleAssets(int bookId, DateOnly month) =>
        db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Include(a => a.LinkedAsset) // soft-deleted linked assets are filtered → nav stays null
            .Where(a => a.BookId == bookId
                && a.Status == FixedAssetStatus.Active
                && a.InServiceDate <= month.AddMonths(1).AddDays(-1)); // in service by month end

    private (decimal Amount, decimal Units) ChargeFor(FixedAsset asset, DateOnly month, decimal accumulated)
    {
        var remaining = asset.DepreciableBase - accumulated;
        decimal amount;
        decimal unitsThisPeriod = 0m;
        if (asset.Method == DepreciationMethod.UnitsOfProduction)
        {
            // The filtered include leaves the nav null for soft-deleted assets; the DeletedAt check
            // covers same-context tracker fix-up reattaching an already-tracked deleted instance.
            if (asset.LinkedAsset is null || asset.LinkedAsset.DeletedAt is not null || asset.UsefulLifeUnits is not > 0)
            {
                logger?.LogWarning(
                    "Skipping units-of-production depreciation for fixed asset {FixedAssetId} ({AssetName}) — " +
                    "linked operational asset is missing/deleted or useful-life units are not set.",
                    asset.Id, asset.Name);
                return (0m, 0m);
            }

            unitsThisPeriod = Math.Max(0m, asset.LinkedAsset.CurrentShotCount - asset.LastDepreciatedUnits);
            if (unitsThisPeriod <= 0m)
            {
                logger?.LogDebug(
                    "No new units for fixed asset {FixedAssetId} ({AssetName}) in {PeriodMonth:yyyy-MM} — nothing to depreciate.",
                    asset.Id, asset.Name, month);
                return (0m, 0m);
            }

            // (Cost − Salvage) × unitsThisPeriod / UsefulLifeUnits, capped below at remaining book value.
            amount = Math.Round(asset.DepreciableBase * unitsThisPeriod / asset.UsefulLifeUnits.Value, 2);
        }
        else
        {
            amount = TimeBasedCharge(asset, accumulated, asset.DepreciationEntries.Count);
        }

        amount = Math.Min(amount, remaining);
        return amount > 0m ? (amount, unitsThisPeriod) : (0m, 0m);
    }

    private static FixedAssetDetailModel Detail(FixedAsset asset)
    {
        var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
        return new FixedAssetDetailModel(
            Map(asset, accumulated),
            asset.AssetGlAccountId,
            asset.AccumulatedDepreciationGlAccountId,
            asset.DepreciationExpenseGlAccountId,
            asset.Status == FixedAssetStatus.Active && asset.DepreciationEntries.Count == 0,
            Schedule(asset));
    }

    private static List<DepreciationScheduleRowModel> Schedule(FixedAsset asset)
    {
        var rows = new List<DepreciationScheduleRowModel>();
        var running = 0m;
        foreach (var posted in asset.DepreciationEntries.OrderBy(d => d.PeriodMonth))
        {
            running += posted.Amount;
            rows.Add(new DepreciationScheduleRowModel(
                posted.PeriodMonth, posted.Amount, running, asset.Cost - running, true, posted.JournalEntryId));
        }

        if (asset.Status != FixedAssetStatus.Active || asset.Method == DepreciationMethod.UnitsOfProduction)
            return rows;

        var periods = asset.DepreciationEntries.Count;
        var month = periods > 0
            ? asset.DepreciationEntries.Max(d => d.PeriodMonth).AddMonths(1)
            : new DateOnly(asset.InServiceDate.Year, asset.InServiceDate.Month, 1);
        for (var i = 0; i < MaxProjectedMonths && running < asset.DepreciableBase; i++, month = month.AddMonths(1))
        {
            var charge = TimeBasedCharge(asset, running, periods);
            if (charge <= 0m)
                break;

            running += charge;
            periods++;
            rows.Add(new DepreciationScheduleRowModel(month, charge, running, asset.Cost - running, false, null));
        }

        return rows;
    }

    private static decimal TimeBasedCharge(FixedAsset asset, decimal accumulated, int periodsPosted)
    {
        var remaining = asset.DepreciableBase - accumulated;
        if (remaining <= 0m)
            return 0m;

        return asset.Method == DepreciationMethod.DecliningBalance
            ? asset.DecliningBalanceCharge(accumulated, periodsPosted)
            : Math.Min(asset.MonthlyStraightLine, remaining);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    private static FixedAssetModel Map(FixedAsset a, decimal accumulated) => new(
        a.Id, a.BookId, a.Name, a.AssetTag, a.Cost, a.SalvageValue, a.InServiceDate, a.UsefulLifeMonths,
        a.Method == DepreciationMethod.DecliningBalance
            ? a.DecliningBalanceCharge(accumulated, a.DepreciationEntries.Count)
            : a.MonthlyStraightLine,
        accumulated, a.Cost - accumulated, a.Status, a.Method, a.LinkedAssetId, a.UsefulLifeUnits);
}
