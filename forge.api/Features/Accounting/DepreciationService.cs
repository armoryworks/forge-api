using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class DepreciationService(AppDbContext db, IPostingEngine postingEngine) : IDepreciationService
{
    public async Task<FixedAssetModel> CreateAssetAsync(CreateFixedAssetModel model, CancellationToken ct = default)
    {
        if (model.UsefulLifeMonths <= 0)
            throw new InvalidOperationException("Useful life must be a positive number of months.");
        if (model.Cost < model.SalvageValue)
            throw new InvalidOperationException("Salvage value cannot exceed cost.");

        var asset = new FixedAsset
        {
            BookId = model.BookId,
            Name = model.Name,
            AssetTag = model.AssetTag,
            Cost = model.Cost,
            SalvageValue = model.SalvageValue,
            InServiceDate = model.InServiceDate,
            UsefulLifeMonths = model.UsefulLifeMonths,
            Method = DepreciationMethod.StraightLine,
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

            var amount = Math.Min(asset.MonthlyStraightLine, remaining);
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
