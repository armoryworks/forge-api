using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
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
                (v, p) => new InventoryValuationModel(v.PartId, p.PartNumber, v.OnHandQuantity, v.AverageUnitCost, v.TotalValue))
            .OrderBy(m => m.PartNumber)
            .ToListAsync(ct);
    }

    public async Task<InventoryValuationReconciliation> ReconcileAsync(int bookId, CancellationToken ct = default)
    {
        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId).Select(b => (decimal?)b.RoundingTolerance).FirstOrDefaultAsync(ct) ?? 0.01m;

        var storeValue = await db.InventoryValuations
            .Where(v => v.BookId == bookId)
            .SumAsync(v => v.TotalValue, ct);

        // GL inventory-control balance: net debit over the Inventory control accounts (debit-normal asset).
        var glInventory = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.ControlType == ControlAccountType.Inventory
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
             select line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount)
            .SumAsync(ct);

        return new InventoryValuationReconciliation(bookId, storeValue, glInventory, tolerance);
    }
}
