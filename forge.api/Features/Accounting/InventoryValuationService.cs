using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class InventoryValuationService(AppDbContext db) : IInventoryValuationService
{
    public async Task ApplyReceiptAsync(
        int bookId, int partId, decimal quantity, decimal totalCost, CancellationToken ct = default)
    {
        if (quantity <= 0m)
            return;

        var row = await db.InventoryValuations
            .FirstOrDefaultAsync(v => v.BookId == bookId && v.PartId == partId, ct);

        if (row is null)
        {
            row = new InventoryValuation { BookId = bookId, PartId = partId };
            db.InventoryValuations.Add(row);
        }

        var newQty = row.OnHandQuantity + quantity;
        var newValue = row.TotalValue + totalCost;
        row.OnHandQuantity = newQty;
        row.TotalValue = newValue;
        row.AverageUnitCost = newQty != 0m ? Math.Round(newValue / newQty, 6) : 0m;

        await db.SaveChangesAsync(ct);
    }

    public async Task<decimal> ApplyIssueAsync(
        int bookId, int partId, decimal quantity, CancellationToken ct = default)
    {
        if (quantity <= 0m)
            return 0m;

        var row = await db.InventoryValuations
            .FirstOrDefaultAsync(v => v.BookId == bookId && v.PartId == partId, ct)
            ?? throw new InvalidOperationException(
                $"No inventory valuation row for part {partId} in book {bookId} to relieve.");

        var relievedValue = Math.Round(quantity * row.AverageUnitCost, 2);
        row.OnHandQuantity -= quantity;
        row.TotalValue -= relievedValue;
        // Average is unchanged by an issue at average; renormalize defensively if quantity hits zero.
        if (row.OnHandQuantity == 0m)
            row.TotalValue = 0m;

        await db.SaveChangesAsync(ct);
        return relievedValue;
    }

    public async Task<IReadOnlyList<InventoryValuationModel>> GetAsync(int bookId, CancellationToken ct = default)
    {
        return await db.InventoryValuations
            .AsNoTracking()
            .Where(v => v.BookId == bookId && v.OnHandQuantity != 0m)
            .Join(db.Set<Forge.Core.Entities.Part>().IgnoreQueryFilters(),
                v => v.PartId, p => p.Id,
                (v, p) => new { v.PartId, p.PartNumber, v.OnHandQuantity, v.AverageUnitCost, v.TotalValue })
            .OrderBy(r => r.PartNumber)
            .Select(r => new InventoryValuationModel(r.PartId, r.PartNumber, r.OnHandQuantity, r.AverageUnitCost, r.TotalValue))
            .ToListAsync(ct);
    }

    public async Task<InventoryValuationReconciliation> ReconcileAsync(int bookId, CancellationToken ct = default)
    {
        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId).Select(b => (decimal?)b.RoundingTolerance).FirstOrDefaultAsync(ct) ?? 0.01m;

        var rules = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId
                && (r.Key == KeyInventoryRaw || r.Key == KeyInventoryWip
                    || r.Key == KeyInventorySubassembly || r.Key == KeyInventoryFg))
            .Select(r => new { r.Key, r.GlAccountId })
            .ToListAsync(ct);
        int? AccountFor(string key) => rules.FirstOrDefault(r => r.Key == key)?.GlAccountId;
        var wipAccountId = AccountFor(KeyInventoryWip);

        var accounts = await db.GlAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.BookId == bookId && a.ControlType == ControlAccountType.Inventory)
            .Select(a => new { a.Id, a.AccountNumber, a.Name })
            .OrderBy(a => a.AccountNumber)
            .ToListAsync(ct);

        // GL inventory-control balance: net debit over the Inventory control accounts (debit-normal asset).
        var glLines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.Inventory
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
             group line by new { line.GlAccountId, entry.Source, entry.SourceType, line.JobId } into g
             select new
             {
                 g.Key.GlAccountId,
                 g.Key.Source,
                 g.Key.SourceType,
                 g.Key.JobId,
                 Amount = g.Sum(l => l.Debit > 0 ? l.FunctionalAmount : -l.FunctionalAmount),
             })
            .ToListAsync(ct);

        var storeRows = await db.InventoryValuations.AsNoTracking()
            .Where(v => v.BookId == bookId)
            .Join(db.Set<Forge.Core.Entities.Part>().IgnoreQueryFilters(),
                v => v.PartId, p => p.Id,
                (v, p) => new { p.InventoryClass, v.TotalValue })
            .ToListAsync(ct);
        var storeByAccount = storeRows
            .GroupBy(r => AccountFor(StoreKeyFor(r.InventoryClass)))
            .ToDictionary(g => g.Key ?? 0, g => g.Sum(r => r.TotalValue));

        var storeValue = storeRows.Sum(r => r.TotalValue);
        var glInventory = glLines.Sum(l => l.Amount);
        var glWip = glLines.Where(l => l.GlAccountId == wipAccountId).Sum(l => l.Amount);

        var accountLines = new List<InventoryReconciliationAccountLine>();
        var items = new List<InventoryReconcilingItem>();
        foreach (var account in accounts.Where(a => a.Id != wipAccountId))
        {
            var lines = glLines.Where(l => l.GlAccountId == account.Id).ToList();
            var accountStore = storeByAccount.GetValueOrDefault(account.Id);
            var accountGl = lines.Sum(l => l.Amount);
            accountLines.Add(new InventoryReconciliationAccountLine(account.AccountNumber, account.Name, accountStore, accountGl));

            var explained = 0m;
            foreach (var source in lines
                .Where(l => !FeedsStore(l.SourceType))
                .GroupBy(l => l.SourceType ?? l.Source.ToString())
                .OrderBy(g => g.Key))
            {
                var amount = source.Sum(l => l.Amount);
                if (amount == 0m)
                    continue;
                explained -= amount;
                items.Add(new InventoryReconcilingItem(
                    "PostedOutsideStore", account.AccountNumber, source.Key, -amount,
                    $"{source.Key} posted {amount:0.00} to {account.AccountNumber} {account.Name} without going through the valuation store."));
            }

            var remainder = Math.Round(accountStore - accountGl - explained, 2);
            if (remainder != 0m)
                items.Add(new InventoryReconcilingItem(
                    "Unexplained", account.AccountNumber, null, remainder,
                    $"The valuation store and {account.AccountNumber} {account.Name} differ by {remainder:0.00} on movements posted through the store, such as a relief of a part with no store row."));
        }

        var unmappedStore = storeByAccount
            .Where(kv => kv.Key == 0 || accounts.All(a => a.Id != kv.Key) || kv.Key == wipAccountId)
            .Sum(kv => kv.Value);
        if (unmappedStore != 0m)
            items.Add(new InventoryReconcilingItem(
                "Unexplained", null, null, Math.Round(unmappedStore, 2),
                $"Valuation-store value of {unmappedStore:0.00} belongs to parts whose inventory class maps to no inventory control account in this book."));

        var wipByJob = glLines
            .Where(l => l.GlAccountId == wipAccountId)
            .GroupBy(l => l.JobId)
            .Select(g => new InventoryWorkInProcessByJob(g.Key, g.Sum(l => l.Amount)))
            .Where(j => j.Amount != 0m)
            .OrderBy(j => j.JobId)
            .ToList();

        return new InventoryValuationReconciliation(bookId, storeValue, glInventory, tolerance)
        {
            GlWorkInProcessBalance = glWip,
            Accounts = accountLines,
            ReconcilingItems = items,
            WorkInProcessByJob = wipByJob,
        };
    }

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";

    private static readonly HashSet<string> StoreFedSourceTypes = new(StringComparer.Ordinal)
    {
        "Receipt", "ProductionRun", "MaterialIssue", "Invoice", "CycleCountLine", "ScanCount", "ScanCountReversal", "InterPlantTransferLine",
    };

    private static bool FeedsStore(string? sourceType) => sourceType is not null && StoreFedSourceTypes.Contains(sourceType);

    private static string StoreKeyFor(InventoryClass inventoryClass) => inventoryClass switch
    {
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.FinishedGood => KeyInventoryFg,
        _ => KeyInventoryRaw,
    };
}
