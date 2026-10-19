using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// One stock quantity correction to post: an approved cycle-count variance, a scanner count or its reversal, or
/// a shortage found when an inter-plant transfer is received. <see cref="QuantityChange"/> is signed: negative for a
/// shortage, positive for an overage. <see cref="SourceType"/> and <see cref="SourceId"/> name the record the
/// change came from and make the posting idempotent.
/// </summary>
public sealed record InventoryAdjustmentPosting(
    string SourceType,
    long SourceId,
    int PartId,
    decimal QuantityChange,
    DateOnly EntryDate,
    int ActingUserId,
    string Reference);

/// <summary>
/// Posts the ledger side of a counted or lost stock quantity, so the inventory control accounts and the
/// valuation store follow the bins:
/// <list type="bullet">
///   <item><b>Shortage</b> → Dr INVENTORY_WRITEDOWN / Cr INVENTORY_{RAW|SUBASSEMBLY|FG} (the part's class).</item>
///   <item><b>Overage</b> → Dr INVENTORY_{RAW|SUBASSEMBLY|FG} / Cr INVENTORY_WRITEDOWN.</item>
/// </list>
/// Valued at the valuation store's weighted average when the part has a store row (and the store is relieved or
/// re-credited), else at the part's resolved standard cost with the store left untouched.
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF.</b> A non-stocked part, a part with no cost, or a book
/// without the INVENTORY_WRITEDOWN rule is skipped and logged; the count itself is never blocked. Idempotent on
/// the source record: a re-post is a no-op before the store is touched.</para>
/// </summary>
public interface IInventoryAdjustmentPostingService
{
    /// <summary>
    /// Posts the inventory adjustment journal for one quantity change, when (and only when) CAP-ACCT-FULLGL is
    /// enabled.
    /// </summary>
    Task PostAdjustmentAsync(InventoryAdjustmentPosting adjustment, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class InventoryAdjustmentPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IInventoryValuationService? valuation = null) : IInventoryAdjustmentPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";
    private const string KeyInventoryWritedown = "INVENTORY_WRITEDOWN";

    public async Task PostAdjustmentAsync(InventoryAdjustmentPosting adjustment, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability) || adjustment.QuantityChange == 0m)
            return;

        var part = await db.Parts
            .Include(p => p.CurrentCostCalculation)
            .FirstOrDefaultAsync(p => p.Id == adjustment.PartId, ct);
        if (!IsStocked(part))
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the inventory adjustment into.");

        var idempotencyKey = $"{JournalSource.Inventory}:{adjustment.SourceType}:{adjustment.SourceId}:ADJUST";
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);
        if (alreadyPosted)
            return;

        var inventoryKey = InventoryKeyFor(part!);
        var mappedKeys = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == book.Id && (r.Key == KeyInventoryWritedown || r.Key == inventoryKey))
            .Select(r => r.Key)
            .Distinct()
            .CountAsync(ct);
        if (mappedKeys < 2)
        {
            Log.Warning(
                "Inventory adjustment GL skipped for {SourceType} {SourceId}: book {BookId} has no {WritedownKey} or {InventoryKey} rule.",
                adjustment.SourceType, adjustment.SourceId, book.Id, KeyInventoryWritedown, inventoryKey);
            return;
        }

        var quantity = Math.Abs(adjustment.QuantityChange);
        var isShortage = adjustment.QuantityChange < 0m;
        var storeRow = valuation is null
            ? null
            : await db.InventoryValuations.AsNoTracking()
                .FirstOrDefaultAsync(v => v.BookId == book.Id && v.PartId == part!.Id, ct);
        var standard = part!.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount ?? 0m;

        decimal amount;
        if (isShortage)
        {
            amount = storeRow is not null
                ? await valuation!.ApplyIssueAsync(book.Id, part.Id, quantity, ct)
                : Math.Round(standard * quantity, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            var unitCost = storeRow is { AverageUnitCost: > 0m } ? storeRow.AverageUnitCost : standard;
            amount = Math.Round(unitCost * quantity, 2, MidpointRounding.AwayFromZero);
            if (amount > 0m && valuation is not null)
                await valuation.ApplyReceiptAsync(book.Id, part.Id, quantity, amount, ct);
        }

        if (amount <= 0m)
        {
            Log.Information(
                "Inventory adjustment GL skipped for {SourceType} {SourceId}: part {PartNumber} has no cost to value it at.",
                adjustment.SourceType, adjustment.SourceId, part.PartNumber);
            return;
        }

        var (debitKey, creditKey) = isShortage
            ? (KeyInventoryWritedown, inventoryKey)
            : (inventoryKey, KeyInventoryWritedown);
        var verb = isShortage ? "shortage" : "overage";
        var desc = $"Inventory {verb} — {adjustment.Reference}, {part.PartNumber} x{quantity}";

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = adjustment.EntryDate,
            Source = JournalSource.Inventory,
            SourceType = adjustment.SourceType,
            SourceId = adjustment.SourceId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Inventory {verb} — {adjustment.Reference}, {part.PartNumber}",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                new PostingLine { AccountKey = debitKey, Debit = amount, Description = desc },
                new PostingLine { AccountKey = creditKey, Credit = amount, Description = desc },
            ],
        }, adjustment.ActingUserId, ct);
    }

    /// <summary>True for perpetual-stocked inventory classes (the ones backed by an INVENTORY_* account).</summary>
    private static bool IsStocked(Part? part) => part?.InventoryClass is
        InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    /// <summary>Maps the part's <see cref="InventoryClass"/> to the inventory determination key it sits in.</summary>
    private static string InventoryKeyFor(Part part) => part.InventoryClass switch
    {
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.FinishedGood => KeyInventoryFg,
        _ => KeyInventoryRaw,
    };
}
