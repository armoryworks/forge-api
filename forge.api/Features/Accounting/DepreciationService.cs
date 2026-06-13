using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class DepreciationService(
    AppDbContext db, IPostingEngine postingEngine, ILogger<DepreciationService>? logger = null) : IDepreciationService
{
    public async Task<FixedAssetModel> CreateAssetAsync(CreateFixedAssetModel model, CancellationToken ct = default)
    {
        if (model.UsefulLifeMonths <= 0)
            throw new InvalidOperationException("Useful life must be a positive number of months.");
        if (model.Cost < model.SalvageValue)
            throw new InvalidOperationException("Salvage value cannot exceed cost.");

        if (model.Method == DepreciationMethod.UnitsOfProduction)
        {
            if (model.UsefulLifeUnits is not > 0)
                throw new InvalidOperationException(
                    "Units-of-production depreciation requires a positive useful life in units (total expected shots).");
            if (model.LinkedAssetId is not int linkedAssetId)
                throw new InvalidOperationException(
                    "Units-of-production depreciation requires a linked operational asset supplying the shot count.");

            var linked = await db.Assets.AsNoTracking()
                .Where(a => a.Id == linkedAssetId)
                .Select(a => new { a.IsCustomerOwned })
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Linked operational asset {linkedAssetId} not found.");

            if (linked.IsCustomerOwned)
                throw new InvalidOperationException(
                    "Customer-owned tooling cannot be capitalized — it stays off the balance sheet and is " +
                    "memo-tracked operationally only. Link a company-owned asset instead.");
        }

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
            LinkedAssetId = model.Method == DepreciationMethod.UnitsOfProduction ? model.LinkedAssetId : null,
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

        var assets = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Include(a => a.LinkedAsset) // soft-deleted linked assets are filtered → nav stays null
            .Where(a => a.BookId == bookId
                && a.Status == FixedAssetStatus.Active
                && a.InServiceDate <= month.AddMonths(1).AddDays(-1)) // in service by month end
            .ToListAsync(ct);

        var count = 0;
        var total = 0m;

        foreach (var asset in assets)
        {
            // Idempotent: skip a month already posted for this asset.
            if (asset.DepreciationEntries.Any(d => d.PeriodMonth == month))
                continue;

            var accumulated = asset.DepreciationEntries.Sum(d => d.Amount);
            var remaining = asset.DepreciableBase - accumulated;
            if (remaining <= 0m)
            {
                asset.Status = FixedAssetStatus.FullyDepreciated;
                continue;
            }

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
                    continue;
                }

                unitsThisPeriod = Math.Max(0m, asset.LinkedAsset.CurrentShotCount - asset.LastDepreciatedUnits);
                if (unitsThisPeriod <= 0m)
                {
                    logger?.LogDebug(
                        "No new units for fixed asset {FixedAssetId} ({AssetName}) in {PeriodMonth:yyyy-MM} — nothing to depreciate.",
                        asset.Id, asset.Name, month);
                    continue;
                }

                // (Cost − Salvage) × unitsThisPeriod / UsefulLifeUnits, capped below at remaining book value.
                amount = Math.Round(asset.DepreciableBase * unitsThisPeriod / asset.UsefulLifeUnits.Value, 2);
            }
            else
            {
                amount = asset.MonthlyStraightLine;
            }

            amount = Math.Min(amount, remaining);
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
        }

        await db.SaveChangesAsync(ct);
        return new DepreciationRunResult(bookId, month, count, total);
    }

    private static FixedAssetModel Map(FixedAsset a, decimal accumulated) => new(
        a.Id, a.BookId, a.Name, a.AssetTag, a.Cost, a.SalvageValue, a.InServiceDate, a.UsefulLifeMonths,
        a.MonthlyStraightLine, accumulated, a.Cost - accumulated, a.Status);
}
