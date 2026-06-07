using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class BankReconciliationService(AppDbContext db, IClock clock) : IBankReconciliationService
{
    public async Task<BankReconciliationWorksheet> StartAsync(
        int bookId, int cashGlAccountId, DateOnly statementDate, decimal statementEndingBalance, CancellationToken ct = default)
    {
        var account = await db.GlAccounts.FirstOrDefaultAsync(a => a.Id == cashGlAccountId && a.BookId == bookId, ct)
            ?? throw new KeyNotFoundException($"GL account {cashGlAccountId} not found in book {bookId}.");

        // Lines already cleared by a finalized rec are settled — exclude them from the new worksheet.
        var settledLineIds = await
            (from item in db.BankReconciliationItems
             join priorRec in db.BankReconciliations on item.BankReconciliationId equals priorRec.Id
             where priorRec.CashGlAccountId == cashGlAccountId
                 && priorRec.Status == BankReconciliationStatus.Finalized
                 && item.IsCleared
             select item.JournalLineId)
            .ToListAsync(ct);
        var settled = settledLineIds.ToHashSet();

        var cashLineIds = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             where entry.BookId == bookId
                 && line.GlAccountId == cashGlAccountId
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= statementDate
             select line.Id)
            .ToListAsync(ct);

        var rec = new BankReconciliation
        {
            BookId = bookId,
            CashGlAccountId = cashGlAccountId,
            StatementDate = statementDate,
            StatementEndingBalance = statementEndingBalance,
            Status = BankReconciliationStatus.Draft,
            Items = cashLineIds
                .Where(id => !settled.Contains(id))
                .Select(id => new BankReconciliationItem { JournalLineId = id, IsCleared = false })
                .ToList(),
        };
        db.BankReconciliations.Add(rec);
        await db.SaveChangesAsync(ct);

        return await BuildWorksheetAsync(rec.Id, ct);
    }

    public Task<BankReconciliationWorksheet> GetWorksheetAsync(int reconciliationId, CancellationToken ct = default)
        => BuildWorksheetAsync(reconciliationId, ct);

    public async Task<BankReconciliationWorksheet> SetClearedAsync(
        int reconciliationId, long journalLineId, bool isCleared, CancellationToken ct = default)
    {
        var rec = await db.BankReconciliations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == reconciliationId, ct)
            ?? throw new KeyNotFoundException($"Bank reconciliation {reconciliationId} not found.");

        if (rec.Status != BankReconciliationStatus.Draft)
            throw new InvalidOperationException("Only a Draft reconciliation can be edited.");

        var item = rec.Items.FirstOrDefault(i => i.JournalLineId == journalLineId)
            ?? throw new KeyNotFoundException($"Journal line {journalLineId} is not on reconciliation {reconciliationId}.");

        item.IsCleared = isCleared;
        await db.SaveChangesAsync(ct);

        return await BuildWorksheetAsync(reconciliationId, ct);
    }

    public async Task<BankReconciliationWorksheet> FinalizeAsync(int reconciliationId, CancellationToken ct = default)
    {
        var rec = await db.BankReconciliations.FirstOrDefaultAsync(r => r.Id == reconciliationId, ct)
            ?? throw new KeyNotFoundException($"Bank reconciliation {reconciliationId} not found.");

        if (rec.Status == BankReconciliationStatus.Finalized)
            throw new InvalidOperationException("Reconciliation is already finalized.");

        var worksheet = await BuildWorksheetAsync(reconciliationId, ct);
        if (!worksheet.IsReconciled)
            throw new InvalidOperationException(
                $"Cannot finalize: the reconciliation is out of balance by {worksheet.Difference:0.00}.");

        rec.Status = BankReconciliationStatus.Finalized;
        rec.FinalizedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return await BuildWorksheetAsync(reconciliationId, ct);
    }

    private async Task<BankReconciliationWorksheet> BuildWorksheetAsync(int reconciliationId, CancellationToken ct)
    {
        var rec = await db.BankReconciliations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reconciliationId, ct)
            ?? throw new KeyNotFoundException($"Bank reconciliation {reconciliationId} not found.");

        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == rec.BookId).Select(b => (decimal?)b.RoundingTolerance).FirstOrDefaultAsync(ct) ?? 0.01m;

        // GL cash balance as of the statement date (Σ net debit over all cash lines, not just this rec's).
        var bookBalance = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             where entry.BookId == rec.BookId
                 && line.GlAccountId == rec.CashGlAccountId
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.EntryDate <= rec.StatementDate
             select line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount)
            .SumAsync(ct);

        var rows = await
            (from item in db.BankReconciliationItems
             join line in db.JournalLines.IgnoreQueryFilters() on item.JournalLineId equals line.Id
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             where item.BankReconciliationId == reconciliationId
             orderby entry.EntryDate, line.Id
             select new BankReconciliationItemRow
             {
                 ItemId = item.Id,
                 JournalLineId = line.Id,
                 JournalEntryId = entry.Id,
                 EntryDate = entry.EntryDate,
                 Description = line.Description,
                 Amount = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
                 IsCleared = item.IsCleared,
             })
            .ToListAsync(ct);

        return new BankReconciliationWorksheet
        {
            ReconciliationId = rec.Id,
            BookId = rec.BookId,
            CashGlAccountId = rec.CashGlAccountId,
            StatementDate = rec.StatementDate,
            StatementEndingBalance = rec.StatementEndingBalance,
            Status = rec.Status,
            BookBalance = bookBalance,
            Items = rows,
            ClearedTotal = rows.Where(r => r.IsCleared).Sum(r => r.Amount),
            OutstandingTotal = rows.Where(r => !r.IsCleared).Sum(r => r.Amount),
            RoundingTolerance = tolerance,
        };
    }
}
