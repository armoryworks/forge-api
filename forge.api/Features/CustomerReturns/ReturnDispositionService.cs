using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.CustomerReturns;

/// <summary>
/// The stock and ledger side of a customer return's disposition, independent of any credit memo:
/// <list type="bullet">
///   <item><b>ReturnToStock</b>: the goods go back into a bin (BinMovement reason Return) under the part's shipping
///         lock, into the valuation store at their original cost, and Dr INVENTORY_{class} / Cr COGS at that cost
///         (<c>Inventory:CustomerReturn:{id}:RESTOCK</c>). When the sale posted no COGS, the offset is Inventory
///         Write-Down, as for a count overage.</item>
///   <item><b>Scrap</b>: no stock; Dr INVENTORY_WRITEDOWN / Cr COGS at the original cost
///         (<c>Inventory:CustomerReturn:{id}:SCRAP</c>), nothing when no COGS was posted.</item>
///   <item><b>Rework</b> and <b>NoReturn</b>: no inventory entry.</item>
/// </list>
/// Original unit cost is the invoice's COGS for the line divided by the invoiced quantity, else the part's standard
/// cost, else the valuation store's average. Ledger entries stay dark while CAP-ACCT-FULLGL is off.
/// </summary>
public class ReturnDispositionService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IClock clock,
    IInventoryValuationService? valuation = null,
    IStandardCostResolver? standardCost = null)
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeyCogs = "COGS";
    private const string KeyInventoryWritedown = "INVENTORY_WRITEDOWN";

    /// <summary>The part a return brings back: the invoice line's, else the order line's, else the job's.</summary>
    public static int? PartIdFor(CustomerReturn ret)
        => ret.InvoiceLine?.PartId ?? ret.SalesOrderLine?.PartId ?? ret.OriginalJob?.PartId;

    public async Task ApplyAsync(
        CustomerReturn ret, ReturnDisposition disposition, decimal quantity, int? locationId, string? lotNumber, CancellationToken ct)
    {
        ret.Disposition = disposition;
        var partId = PartIdFor(ret);

        switch (disposition)
        {
            case ReturnDisposition.ReturnToStock:
                if (partId is not int restockPart)
                    throw new InvalidOperationException($"Return {ret.ReturnNumber} has no part to put back into stock.");
                await RestockAsync(ret, restockPart, quantity, locationId!.Value, lotNumber, ct);
                break;
            case ReturnDisposition.Scrap:
                ret.QuantityScrapped = quantity;
                if (partId is int scrapPart)
                    await PostScrapAsync(ret, scrapPart, quantity, ct);
                break;
        }
    }

    private async Task RestockAsync(CustomerReturn ret, int partId, decimal quantity, int locationId, string? lotNumber, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.AcquireAdvisoryLockAsync("inventory.ship-relief", partId.ToString(CultureInfo.InvariantCulture), ct);

        var now = clock.UtcNow;
        var userId = db.CurrentUserId ?? 0;
        var lot = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        var bin = await db.BinContents.FirstOrDefaultAsync(c => c.EntityType == "part" && c.EntityId == partId
            && c.LocationId == locationId && c.LotNumber == lot && c.Status == BinContentStatus.Stored
            && c.RemovedAt == null, ct);
        if (bin is null)
        {
            db.BinContents.Add(new BinContent
            {
                EntityType = "part",
                EntityId = partId,
                LocationId = locationId,
                Quantity = quantity,
                LotNumber = lot,
                Status = BinContentStatus.Stored,
                PlacedBy = userId,
                PlacedAt = now,
                Notes = $"Customer return {ret.ReturnNumber}",
            });
        }
        else
        {
            bin.Quantity += quantity;
        }

        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = partId,
            Quantity = quantity,
            LotNumber = lot,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Return,
            Notes = $"Customer return {ret.ReturnNumber}",
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        ret.QuantityRestocked = quantity;
        ret.RestockLocationId = locationId;
        ret.RestockLotNumber = lot;
        ret.RestockBinMovementId = movement.Id;

        await PostRestockAsync(ret, partId, quantity, ct);
    }

    private async Task PostRestockAsync(CustomerReturn ret, int partId, decimal quantity, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var (part, book) = await StockedPartAndBookAsync(partId, ct);
        if (part is null || book is null)
            return;

        var key = $"{JournalSource.Inventory}:CustomerReturn:{ret.Id}:RESTOCK";
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var cost = await OriginalUnitCostAsync(ret, part, book.Id, ct);
        var value = Money.Round(cost.UnitCost * quantity);
        if (value <= 0m)
        {
            Log.Information("Return {ReturnNumber}: no cost to value restocked part {PartNumber} at; ledger skipped.", ret.ReturnNumber, part.PartNumber);
            return;
        }

        if (valuation is not null)
            await valuation.ApplyReceiptAsync(book.Id, part.Id, quantity, value, ct);

        var inventoryKey = InventoryValuationService.StoreKeyFor(part.InventoryClass);
        var description = $"Return {ret.ReturnNumber} to stock — {part.PartNumber} x{MoneyQuantity(quantity)}";
        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            Source = JournalSource.Inventory,
            SourceType = "CustomerReturn",
            SourceId = ret.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Customer return {ret.ReturnNumber} returned to stock",
            IdempotencyKey = key,
            Lines =
            [
                new PostingLine { AccountKey = inventoryKey, Debit = value, Description = description },
                new PostingLine { AccountKey = cost.FromCogs ? KeyCogs : KeyInventoryWritedown, Credit = value, Description = description },
            ],
        }, db.CurrentUserId ?? 0, ct);
    }

    private async Task PostScrapAsync(CustomerReturn ret, int partId, decimal quantity, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var (part, book) = await StockedPartAndBookAsync(partId, ct);
        if (part is null || book is null)
            return;

        var key = $"{JournalSource.Inventory}:CustomerReturn:{ret.Id}:SCRAP";
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var cost = await OriginalUnitCostAsync(ret, part, book.Id, ct);
        if (!cost.FromCogs)
            return;

        var value = Money.Round(cost.UnitCost * quantity);
        if (value <= 0m)
            return;

        var description = $"Return {ret.ReturnNumber} scrapped — {part.PartNumber} x{MoneyQuantity(quantity)}";
        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            Source = JournalSource.Inventory,
            SourceType = "CustomerReturn",
            SourceId = ret.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Customer return {ret.ReturnNumber} scrapped",
            IdempotencyKey = key,
            Lines =
            [
                new PostingLine { AccountKey = KeyInventoryWritedown, Debit = value, Description = description },
                new PostingLine { AccountKey = KeyCogs, Credit = value, Description = description },
            ],
        }, db.CurrentUserId ?? 0, ct);
    }

    /// <summary>
    /// Per-unit cost the returned goods carry: the invoice line's posted COGS ÷ invoiced quantity (<c>FromCogs</c>),
    /// else the part's standard cost, else the valuation store's average.
    /// </summary>
    internal async Task<(decimal UnitCost, bool FromCogs)> OriginalUnitCostAsync(CustomerReturn ret, Part part, int bookId, CancellationToken ct)
    {
        if (ret.InvoiceLine is { } line && line.Quantity > 0m)
        {
            var invoiceNumber = await db.Invoices.AsNoTracking()
                .Where(i => i.Id == line.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct);
            var key = $"{JournalSource.Inventory}:Invoice:{line.InvoiceId}:COGS";
            var prefix = $"COGS — invoice {invoiceNumber} line {line.LineNumber}:";
            var cogs = await db.JournalLines.AsNoTracking()
                .Where(l => l.JournalEntry.BookId == bookId && l.JournalEntry.IdempotencyKey == key
                    && l.JournalEntry.Status == JournalEntryStatus.Posted
                    && l.Debit > 0m && l.Description != null && l.Description.StartsWith(prefix))
                .SumAsync(l => l.Debit, ct);
            if (cogs > 0m)
                return (cogs / line.Quantity, true);
        }

        if (standardCost is not null)
        {
            var standard = (await standardCost.ResolveAsync(part.Id, ct)).Total;
            if (standard > 0m)
                return (standard, false);
        }

        var fallback = part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount;
        if (fallback is > 0m)
            return (fallback.Value, false);

        var average = await db.InventoryValuations.AsNoTracking()
            .Where(v => v.BookId == bookId && v.PartId == part.Id)
            .Select(v => (decimal?)v.AverageUnitCost)
            .FirstOrDefaultAsync(ct);
        return (average ?? 0m, false);
    }

    private async Task<(Part? Part, Book? Book)> StockedPartAndBookAsync(int partId, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking()
            .Include(p => p.CurrentCostCalculation)
            .FirstOrDefaultAsync(p => p.Id == partId, ct);
        if (part is null
            || part.ProcurementSource == ProcurementSource.Phantom
            || part.InventoryClass is not (InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood))
            return (null, null);

        var book = await db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the return into.");
        return (part, book);
    }

    private static string MoneyQuantity(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);
}
