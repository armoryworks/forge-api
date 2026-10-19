using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// NCR scrap posting. When a non-conformance is dispositioned as Scrap and held stock of its lot is drawn down,
/// this service writes the scrapped value off <b>inline, in the disposition's transaction</b>:
/// Dr INVENTORY_WRITEDOWN / Cr INVENTORY_{RAW|SUBASSEMBLY|FG} (the part's own class), valued at the
/// <b>perpetual weighted-average</b> from the valuation store, which is relieved by the same amount.
///
/// <para><b>Stays dark while CAP-ACCT-FULLGL is off.</b> A no-op for a non-stocked part or a zero quantity.
/// Idempotent per NCR via the engine's (BookId, IdempotencyKey) de-dupe, with a pre-check so a re-post never
/// relieves the store twice. When the store has no row for the part it falls back to the part's latest
/// purchase price and leaves the store untouched, as the material-issue posting does.</para>
/// </summary>
public interface INcrScrapPostingService
{
    /// <summary>
    /// Posts the write-off for <paramref name="quantity"/> units scrapped by NCR <paramref name="ncrId"/>, when
    /// (and only when) CAP-ACCT-FULLGL is enabled.
    /// </summary>
    Task PostScrapAsync(int ncrId, decimal quantity, DateOnly entryDate, int userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class NcrScrapPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IInventoryValuationService? valuation = null) : INcrScrapPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";
    private const string KeyInventoryWritedown = "INVENTORY_WRITEDOWN";

    public async Task PostScrapAsync(
        int ncrId, decimal quantity, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability) || quantity <= 0m)
            return;

        var ncr = await db.NonConformances
            .Include(n => n.Part)
            .FirstOrDefaultAsync(n => n.Id == ncrId, ct)
            ?? throw new KeyNotFoundException($"NCR {ncrId} not found");

        if (!IsStocked(ncr.Part))
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
            ?? throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the NCR scrap into.");

        var idempotencyKey = $"{JournalSource.Inventory}:NcrScrap:{ncr.Id}";
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);
        if (alreadyPosted)
            return;

        var hasStoreRow = valuation is not null
            && await db.InventoryValuations.AnyAsync(v => v.BookId == book.Id && v.PartId == ncr.PartId, ct);
        decimal amount;
        if (hasStoreRow)
        {
            amount = await valuation!.ApplyIssueAsync(book.Id, ncr.PartId, quantity, ct);
        }
        else
        {
            var unitCost = await db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.PartId == ncr.PartId)
                .OrderByDescending(l => l.Id)
                .Select(l => l.UnitPrice)
                .FirstOrDefaultAsync(ct);
            amount = Math.Round(quantity * unitCost, 2);
        }
        if (amount <= 0m)
            return;

        var partLabel = ncr.Part.PartNumber;
        var lotLabel = string.IsNullOrWhiteSpace(ncr.LotNumber) ? string.Empty : $" lot {ncr.LotNumber}";
        var desc = $"NCR scrap — {ncr.NcrNumber} {partLabel}{lotLabel} x{quantity:0.####}";

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "NonConformance",
            SourceId = ncr.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"NCR scrap — {ncr.NcrNumber}, {partLabel}",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                new PostingLine { AccountKey = KeyInventoryWritedown, Debit = amount, Description = desc },
                new PostingLine { AccountKey = ReliefKeyFor(ncr.Part), Credit = amount, Description = desc },
            ],
        }, userId, ct);
    }

    private static bool IsStocked(Part? part) => part?.InventoryClass is
        InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    private static string ReliefKeyFor(Part part) => part.InventoryClass switch
    {
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.FinishedGood => KeyInventoryFg,
        _ => KeyInventoryRaw,
    };
}
