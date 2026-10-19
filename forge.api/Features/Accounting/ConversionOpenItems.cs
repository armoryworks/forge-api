using Microsoft.EntityFrameworkCore;
using Serilog;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A conversion — builds the AR/AP sub-ledger behind the opening-balance journal. Each party line on the
/// AR control (a customer debit) or the AP control (a vendor credit) becomes one backing document (a Sent
/// invoice or an Approved vendor bill, one line, no tax) plus its open item, so the opening receivables and
/// payables age, take payments and tie to control. The documents are added directly: nothing posts (the
/// opening journal already carries the control balance), and no email or integration event fires.
/// <para>
/// A document is keyed to its journal line through <c>Provider = "Conversion"</c> and
/// <c>ExternalId = "{entryId}:{lineNumber}"</c>, so a re-run never duplicates one. For each party, the
/// documents are created only when the party's control balance minus its open items equals the undocumented
/// opening lines, within the book's rounding tolerance (sub-cent tax residuals of earlier invoices); otherwise
/// the party is skipped with a warning, so an install whose operator already re-keyed the opening invoices or
/// bills by hand never gets a second copy.
/// </para>
/// </summary>
public static class ConversionOpenItems
{
    public const string Provider = "Conversion";

    private const string OpeningBalanceSourceType = "OpeningBalance";

    private sealed record OpeningLine(
        int LineNumber,
        SubledgerPartyType PartyType,
        int PartyId,
        decimal Amount,
        decimal FunctionalAmount,
        string? Description);

    /// <summary>
    /// Creates the missing backing documents and open items for one posted opening-balance entry.
    /// <paramref name="details"/> carries the optional document number and dates per journal line number.
    /// Returns the number of documents created.
    /// </summary>
    public static async Task<int> EnsureForEntryAsync(
        AppDbContext db,
        long entryId,
        IReadOnlyDictionary<int, OpeningBalanceLineModel>? details,
        CancellationToken ct = default)
    {
        var entry = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null || entry.Status != JournalEntryStatus.Posted)
            return 0;

        var lines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where line.JournalEntryId == entryId
                 && account.IsControlAccount
                 && line.SubledgerPartyId != null
                 && ((account.ControlType == ControlAccountType.AR
                         && line.SubledgerPartyType == SubledgerPartyType.Customer && line.Debit > 0m)
                     || (account.ControlType == ControlAccountType.AP
                         && line.SubledgerPartyType == SubledgerPartyType.Vendor && line.Credit > 0m))
             orderby line.LineNumber
             select new OpeningLine(
                 line.LineNumber,
                 line.SubledgerPartyType!.Value,
                 line.SubledgerPartyId!.Value,
                 line.Debit > 0m ? line.Debit : line.Credit,
                 line.FunctionalAmount,
                 line.Description))
            .ToListAsync(ct);
        if (lines.Count == 0)
            return 0;

        var prefix = $"{entryId}:";
        var documented = (await db.Invoices.IgnoreQueryFilters()
                .Where(i => i.Provider == Provider && i.ExternalId != null && i.ExternalId.StartsWith(prefix))
                .Select(i => i.ExternalId!)
                .ToListAsync(ct))
            .Concat(await db.VendorBills.IgnoreQueryFilters()
                .Where(b => b.Provider == Provider && b.ExternalId != null && b.ExternalId.StartsWith(prefix))
                .Select(b => b.ExternalId!)
                .ToListAsync(ct))
            .ToHashSet();

        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == entry.BookId)
            .Select(b => b.RoundingTolerance)
            .FirstOrDefaultAsync(ct);

        var created = 0;
        foreach (var party in lines
                     .Where(l => !documented.Contains(ExternalIdFor(entryId, l.LineNumber)))
                     .GroupBy(l => (l.PartyType, l.PartyId)))
        {
            var pending = party.Sum(l => l.FunctionalAmount);
            var gap = await UndocumentedBalanceAsync(db, entry.BookId, party.Key.PartyType, party.Key.PartyId, ct);
            if (Math.Abs(gap - pending) > tolerance)
            {
                Log.Warning(
                    "Opening-balance entry {EntryId}: {PartyType} {PartyId} has {Pending} of opening lines without documents, " +
                    "but its control balance less its open items is {Gap}; no conversion documents were created for it. " +
                    "Reconcile the party's opening invoices or bills by hand.",
                    entryId, party.Key.PartyType, party.Key.PartyId, pending, gap);
                continue;
            }

            foreach (var line in party)
            {
                OpeningBalanceLineModel? detail = null;
                details?.TryGetValue(line.LineNumber, out detail);
                if (line.PartyType == SubledgerPartyType.Customer)
                    await AddInvoiceAsync(db, entry, line, detail, ct);
                else
                    await AddVendorBillAsync(db, entry, line, detail, ct);
                created++;
            }
        }

        if (created > 0)
            Log.Information(
                "Created {Count} conversion documents with open items for opening-balance entry {EntryId}.",
                created, entryId);
        return created;
    }

    /// <summary>
    /// Boot-time repair for installs that loaded opening balances before the conversion built its
    /// sub-ledger: runs <see cref="EnsureForEntryAsync"/> for every posted opening-balance entry. A no-op once
    /// every opening line has its document, and on installs with no conversion.
    /// </summary>
    public static async Task EnsureAllAsync(AppDbContext db, CancellationToken ct = default)
    {
        var entryIds = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Source == JournalSource.Conversion
                && e.SourceType == OpeningBalanceSourceType
                && e.Status == JournalEntryStatus.Posted)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var id in entryIds)
            await EnsureForEntryAsync(db, id, null, ct);
    }

    /// <summary>
    /// Checks the optional document numbers before the opening journal posts: an AR number must be free
    /// among invoice numbers (at most 20 characters); an AP number must be free among that vendor's
    /// invoice numbers (at most 60 characters).
    /// </summary>
    public static async Task ValidateDocumentNumbersAsync(
        AppDbContext db, IReadOnlyList<OpeningBalanceLineModel> lines, CancellationToken ct = default)
    {
        var numbered = lines.Where(l => !string.IsNullOrWhiteSpace(l.DocumentNumber) && l.PartyId is not null).ToList();

        var duplicate = numbered
            .GroupBy(l => (l.PartyType == SubledgerPartyType.Vendor ? l.PartyId : null, l.PartyType, l.DocumentNumber!.Trim()))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Document number '{duplicate.Key.Item3}' appears on more than one opening-balance line.");

        foreach (var line in numbered)
        {
            var number = line.DocumentNumber!.Trim();
            if (line.PartyType == SubledgerPartyType.Customer)
            {
                if (number.Length > 20)
                    throw new InvalidOperationException($"Invoice number '{number}' is longer than 20 characters.");
                if (await db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.InvoiceNumber == number, ct))
                    throw new InvalidOperationException($"Invoice number '{number}' is already in use.");
            }
            else if (line.PartyType == SubledgerPartyType.Vendor)
            {
                if (number.Length > 60)
                    throw new InvalidOperationException($"Vendor invoice number '{number}' is longer than 60 characters.");
                var vendorId = line.PartyId!.Value;
                if (await db.VendorBills.IgnoreQueryFilters()
                        .AnyAsync(b => b.VendorId == vendorId && b.VendorInvoiceNumber == number, ct))
                    throw new InvalidOperationException(
                        $"Vendor invoice number '{number}' is already recorded for vendor {vendorId}.");
            }
        }
    }

    private static string ExternalIdFor(long entryId, int lineNumber) => $"{entryId}:{lineNumber}";

    private static async Task<decimal> UndocumentedBalanceAsync(
        AppDbContext db, int bookId, SubledgerPartyType partyType, int partyId, CancellationToken ct)
    {
        var controlType = partyType == SubledgerPartyType.Customer ? ControlAccountType.AR : ControlAccountType.AP;
        var lines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && account.IsControlAccount
                 && account.ControlType == controlType
                 && line.SubledgerPartyType == partyType
                 && line.SubledgerPartyId == partyId
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
             select new { line.Debit, line.FunctionalAmount })
            .ToListAsync(ct);

        if (partyType == SubledgerPartyType.Customer)
        {
            var control = lines.Sum(l => l.Debit > 0m ? l.FunctionalAmount : -l.FunctionalAmount);
            var items = (await db.ArOpenItems.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => i.BookId == bookId && i.CustomerId == partyId
                        && i.Status != OpenItemStatus.Closed && i.Status != OpenItemStatus.Voided)
                    .ToListAsync(ct))
                .Sum(i => i.OpenFunctionalAmount);
            return control - items;
        }
        else
        {
            var control = lines.Sum(l => l.Debit > 0m ? -l.FunctionalAmount : l.FunctionalAmount);
            var items = (await db.ApOpenItems.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => i.BookId == bookId && i.VendorId == partyId
                        && i.Status != OpenItemStatus.Closed && i.Status != OpenItemStatus.Voided)
                    .ToListAsync(ct))
                .Sum(i => i.OpenFunctionalAmount);
            return control - items;
        }
    }

    private static DateTimeOffset AtNoonUtc(DateOnly date)
        => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static string Describe(JournalEntry entry, OpeningLine line)
        => string.IsNullOrWhiteSpace(line.Description)
            ? $"Opening balance (journal entry {entry.EntryNumber})"
            : line.Description!;

    private static string? Truncate(string? value, int max)
        => value is { Length: > 0 } v && v.Length > max ? v[..max] : value;

    private static async Task AddInvoiceAsync(
        AppDbContext db, JournalEntry entry, OpeningLine line, OpeningBalanceLineModel? detail, CancellationToken ct)
    {
        var documentDate = AtNoonUtc(detail?.DocumentDate ?? entry.EntryDate);
        var dueDate = AtNoonUtc(detail?.DueDate ?? detail?.DocumentDate ?? entry.EntryDate);
        var number = string.IsNullOrWhiteSpace(detail?.DocumentNumber)
            ? await DocumentNumberSequence.NextAsync(
                db.Invoices.IgnoreQueryFilters().Select(i => i.InvoiceNumber), "INV", ct)
            : detail!.DocumentNumber!.Trim();
        var description = Describe(entry, line);

        var invoice = new Invoice
        {
            InvoiceNumber = number,
            CustomerId = line.PartyId,
            CurrencyId = entry.CurrencyId,
            FxRate = 1m,
            Status = InvoiceStatus.Sent,
            InvoiceDate = documentDate,
            DueDate = dueDate,
            TaxRate = 0m,
            Notes = $"Opening balance carried over at conversion (journal entry {entry.EntryNumber}).",
            Provider = Provider,
            ExternalId = ExternalIdFor(entry.Id, line.LineNumber),
            ExternalRef = Truncate(description, 100),
            Lines =
            {
                new InvoiceLine
                {
                    Description = description,
                    Quantity = 1m,
                    UnitPrice = line.Amount,
                    LineNumber = 1,
                },
            },
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        db.ArOpenItems.Add(new ArOpenItem
        {
            BookId = entry.BookId,
            CustomerId = line.PartyId,
            SourceType = "Invoice",
            SourceId = invoice.Id,
            DocumentNumber = invoice.InvoiceNumber,
            DocumentDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate,
            CurrencyId = invoice.CurrencyId,
            FxRate = 1m,
            OriginalTxnAmount = line.Amount,
            OriginalFunctionalAmount = line.FunctionalAmount,
            Status = OpenItemStatus.Open,
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task AddVendorBillAsync(
        AppDbContext db, JournalEntry entry, OpeningLine line, OpeningBalanceLineModel? detail, CancellationToken ct)
    {
        var documentDate = AtNoonUtc(detail?.DocumentDate ?? entry.EntryDate);
        var dueDate = AtNoonUtc(detail?.DueDate ?? detail?.DocumentDate ?? entry.EntryDate);
        var number = await DocumentNumberSequence.NextAsync(
            db.VendorBills.IgnoreQueryFilters().Select(b => b.BillNumber), "BILL", ct);
        var description = Describe(entry, line);

        var bill = new VendorBill
        {
            BillNumber = number,
            VendorId = line.PartyId,
            VendorInvoiceNumber = string.IsNullOrWhiteSpace(detail?.DocumentNumber) ? null : detail!.DocumentNumber!.Trim(),
            CurrencyId = entry.CurrencyId,
            FxRate = 1m,
            Status = VendorBillStatus.Approved,
            BillDate = documentDate,
            DueDate = dueDate,
            TaxAmount = 0m,
            Notes = $"Opening balance carried over at conversion (journal entry {entry.EntryNumber}).",
            Provider = Provider,
            ExternalId = ExternalIdFor(entry.Id, line.LineNumber),
            ExternalRef = Truncate(description, 100),
            Lines =
            {
                new VendorBillLine
                {
                    Description = description,
                    Quantity = 1m,
                    UnitPrice = line.Amount,
                    LineNumber = 1,
                },
            },
        };
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync(ct);

        db.ApOpenItems.Add(new ApOpenItem
        {
            BookId = entry.BookId,
            VendorId = line.PartyId,
            SourceType = "VendorBill",
            SourceId = bill.Id,
            DocumentNumber = bill.BillNumber,
            DocumentDate = bill.BillDate,
            DueDate = bill.DueDate,
            CurrencyId = bill.CurrencyId,
            FxRate = 1m,
            OriginalTxnAmount = line.Amount,
            OriginalFunctionalAmount = line.FunctionalAmount,
            Status = OpenItemStatus.Open,
        });
        await db.SaveChangesAsync(ct);
    }
}
