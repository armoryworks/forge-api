using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// AP postings for vendor credit and debit memos and their applications to bills, inline in the operational command's
/// transaction. Every method is a no-op while CAP-ACCT-FULLGL is off; the operational balances (memo open amount, bill
/// balance due) move regardless.
/// <list type="bullet">
///   <item><b>Issue</b> (<c>AP:VendorCreditMemo:{id}:ISSUE</c>, memo currency at the memo rate): Dr AP_CONTROL for the
///         total; Cr each line's account: a return line VENDOR_RETURN_CLEARING, a line crediting a PO-matched bill line
///         PURCHASE_PRICE_VARIANCE (the receipt's GRNI accrual was already cleared by the bill), any other line its GL
///         account or determination key; Cr OPERATING_EXPENSE for the tax (purchase tax is expensed there). Creates
///         the memo's AP open item with sign -1. Return lines then clear the exact value the return shipped at: the
///         difference to the memo's credit posts in a functional entry
///         (<c>AP:VendorCreditMemo:{id}:RETURN_CLEARING</c>) between VENDOR_RETURN_CLEARING and
///         PURCHASE_PRICE_VARIANCE, so the clearing account nets to zero for the credited quantity.</item>
///   <item><b>Application</b>: moves both open items by the amount. No GL when the memo and bill rates match;
///         otherwise <c>AP:VendorCreditMemoApplication:{id}:FX</c> Dr AP (bill side) / Cr AP (memo side) with the
///         difference to FX_GAIN or FX_LOSS.</item>
///   <item><b>Void</b>: reverses the issue entries in their own period (the void date when that period is
///         hard-closed) and voids the open item.</item>
///   <item><b>Return shipped</b> (<c>Inventory:VendorReturn:{id}:SHIP</c>, functional): Dr VENDOR_RETURN_CLEARING /
///         Cr the part's inventory account at the lot's receipt cost, taking the same value out of the valuation
///         store.</item>
/// </list>
/// </summary>
public interface IVendorCreditMemoPostingService
{
    /// <summary>Posts an issued memo and creates its AP open item. Idempotent. Returns the issue entry id, if any.</summary>
    Task<long?> PostIssueAsync(int vendorCreditMemoId, int issuedByUserId, CancellationToken ct = default);

    /// <summary>Reverses the issue entries of a memo being voided and voids its open item.</summary>
    Task ReverseIssueAsync(int vendorCreditMemoId, DateOnly voidDate, int voidedByUserId, CancellationToken ct = default);

    /// <summary>Moves the memo's and the bill's open items for an application and posts any FX difference.</summary>
    Task PostApplicationAsync(int applicationId, int appliedByUserId, CancellationToken ct = default);

    /// <summary>Undoes <see cref="PostApplicationAsync"/> for an application being unapplied.</summary>
    Task ReverseApplicationAsync(int applicationId, DateOnly reversalDate, int reversedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Posts a shipped return to vendor and relieves the valuation store; returns the functional value relieved per
    /// return line id. Idempotent; empty while CAP-ACCT-FULLGL is off.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> PostReturnShippedAsync(int vendorReturnId, DateOnly shipDate, int userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VendorCreditMemoPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IInventoryValuationService? valuation = null) : IVendorCreditMemoPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyApControl = "AP_CONTROL";
    private const string KeyPurchaseTaxExpense = "OPERATING_EXPENSE";
    private const string KeyPpv = "PURCHASE_PRICE_VARIANCE";
    private const string KeyFreightIn = "FREIGHT_IN";
    private const string KeyFxGain = "FX_GAIN";
    private const string KeyFxLoss = "FX_LOSS";
    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventorySubassembly = "INVENTORY_SUBASSEMBLY";
    private const string KeyInventoryFg = "INVENTORY_FG";

    /// <summary>The determination key of the account a shipped return to vendor waits in until the vendor credits it.</summary>
    public const string KeyVendorReturnClearing = "VENDOR_RETURN_CLEARING";

    internal const string OpenItemSourceType = "VendorCreditMemo";

    internal static string IssueKey(int memoId) => $"{JournalSource.AP}:VendorCreditMemo:{memoId}:ISSUE";

    internal static string ReturnClearingKey(int memoId) => $"{JournalSource.AP}:VendorCreditMemo:{memoId}:RETURN_CLEARING";

    internal static string ApplicationKey(int applicationId) => $"{JournalSource.AP}:VendorCreditMemoApplication:{applicationId}:FX";

    internal static string ReturnShipKey(int vendorReturnId) => $"{JournalSource.Inventory}:VendorReturn:{vendorReturnId}:SHIP";

    public async Task<long?> PostIssueAsync(int vendorCreditMemoId, int issuedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return null;

        var memo = await db.VendorCreditMemos
            .Include(m => m.Lines).ThenInclude(l => l.VendorBillLine)
            .Include(m => m.Lines).ThenInclude(l => l.VendorReturnLine)
            .FirstOrDefaultAsync(m => m.Id == vendorCreditMemoId, ct);
        if (memo is null)
        {
            Log.Warning("Vendor credit memo posting skipped: memo {MemoId} not found (FULLGL on).", vendorCreditMemoId);
            return null;
        }

        var book = await RequireBookAsync(ct);
        var key = IssueKey(memo.Id);
        var existing = await db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.BookId == book.Id && e.IdempotencyKey == key)
            .Select(e => (long?)e.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var total = memo.Total;
        if (total <= 0m)
            return null;

        if (!await db.ApOpenItems.IgnoreQueryFilters()
                .AnyAsync(i => i.SourceType == OpenItemSourceType && i.SourceId == memo.Id, ct))
        {
            db.ApOpenItems.Add(new ApOpenItem
            {
                BookId = book.Id,
                VendorId = memo.VendorId,
                SourceType = OpenItemSourceType,
                SourceId = memo.Id,
                DocumentNumber = memo.MemoNumber,
                DocumentDate = memo.MemoDate,
                DueDate = memo.MemoDate,
                CurrencyId = memo.CurrencyId,
                FxRate = memo.FxRate,
                OriginalTxnAmount = total,
                OriginalFunctionalAmount = Money.Round(total * memo.FxRate),
                Sign = -1,
                Status = OpenItemStatus.Open,
            });
        }

        var entryDate = DateOnly.FromDateTime(memo.MemoDate.UtcDateTime);
        var entry = await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "VendorCreditMemo",
            SourceId = memo.Id,
            CurrencyId = memo.CurrencyId,
            FxRate = memo.FxRate,
            Memo = memo.Kind == VendorCreditMemoKind.DebitMemo
                ? $"Debit memo {memo.MemoNumber}"
                : $"Vendor credit memo {memo.MemoNumber}" + (memo.VendorReference is { Length: > 0 } r ? $" (vendor ref {r})" : string.Empty),
            IdempotencyKey = key,
            Lines = BuildIssueLines(memo, total),
        }, issuedByUserId, ct);

        await PostReturnClearingDifferenceAsync(memo, book, entryDate, issuedByUserId, ct);
        return entry.Id;
    }

    /// <summary>
    /// The issue lines: Dr AP for the total, Cr each line's account for its amount rounded to the cent (the cents
    /// between the rounded total less tax and the Σ of the rounded lines go on the largest line), Cr the tax.
    /// </summary>
    internal static List<PostingLine> BuildIssueLines(VendorCreditMemo memo, decimal total)
    {
        var number = memo.MemoNumber;
        var tax = Money.Round(memo.TaxAmount);
        var ordered = memo.Lines.OrderBy(l => l.LineNumber).ToList();
        var amounts = ordered.Select(l => l.LineTotal).ToArray();
        var residual = total - tax - amounts.Sum();
        if (residual != 0m && amounts.Length > 0)
        {
            var largest = 0;
            for (var i = 1; i < amounts.Length; i++)
                if (amounts[i] > amounts[largest]) largest = i;
            amounts[largest] += residual;
        }

        var lines = new List<PostingLine>(ordered.Count + 2)
        {
            new()
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = memo.VendorId,
                Debit = total,
                Description = $"AP credit — vendor memo {number}",
            },
        };

        for (var i = 0; i < ordered.Count; i++)
        {
            if (amounts[i] <= 0m)
                continue;
            var line = ordered[i];
            var (accountKey, accountId) = CreditAccount(line);
            lines.Add(new PostingLine
            {
                AccountKey = accountId is null ? accountKey : null,
                GlAccountId = accountId,
                Credit = amounts[i],
                Description = $"Vendor memo {number} line {line.LineNumber}: {line.Description}",
            });
        }

        if (tax > 0m)
            lines.Add(new PostingLine { AccountKey = KeyPurchaseTaxExpense, Credit = tax, Description = $"Purchase tax credited — vendor memo {number}" });
        return lines;
    }

    /// <summary>The account a memo line credits: see the class summary.</summary>
    internal static (string Key, int? AccountId) CreditAccount(VendorCreditMemoLine line)
    {
        if (line.VendorReturnLineId is not null)
            return (KeyVendorReturnClearing, null);
        if (line.VendorBillLine is { } billLine)
        {
            if (billLine.PurchaseOrderLineId is not null)
                return (KeyPpv, null);
            if (billLine.IsFreight && billLine.FreightForPurchaseOrderId is not null)
                return (KeyFreightIn, null);
            return (billLine.AccountDeterminationKey, billLine.GlAccountId);
        }
        return (line.AccountDeterminationKey, line.GlAccountId);
    }

    private async Task PostReturnClearingDifferenceAsync(
        VendorCreditMemo memo, Book book, DateOnly entryDate, int userId, CancellationToken ct)
    {
        var returnLines = memo.Lines.Where(l => l.VendorReturnLine is { ShippedCost: not null }).ToList();
        if (returnLines.Count == 0)
            return;

        var credited = returnLines.Sum(l => FxMath.ToFunctional(l.LineTotal, memo.FxRate));
        var shipped = returnLines.Sum(l => Money.Round(l.VendorReturnLine!.ShippedCost!.Value * l.Quantity / l.VendorReturnLine.Quantity));
        var difference = shipped - credited;
        if (difference == 0m)
            return;

        var number = memo.MemoNumber;
        var lines = difference > 0m
            ? new List<PostingLine>
            {
                new() { AccountKey = KeyPpv, Debit = difference, Description = $"Return credited below its shipped value — vendor memo {number}" },
                new() { AccountKey = KeyVendorReturnClearing, Credit = difference, Description = $"Vendor returns clearing — vendor memo {number}" },
            }
            : new List<PostingLine>
            {
                new() { AccountKey = KeyVendorReturnClearing, Debit = -difference, Description = $"Vendor returns clearing — vendor memo {number}" },
                new() { AccountKey = KeyPpv, Credit = -difference, Description = $"Return credited above its shipped value — vendor memo {number}" },
            };

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "VendorCreditMemoReturn",
            SourceId = memo.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Vendor memo {number}: returns clearing settled at the shipped value",
            IdempotencyKey = ReturnClearingKey(memo.Id),
            Lines = lines,
        }, userId, ct);
    }

    public async Task ReverseIssueAsync(int vendorCreditMemoId, DateOnly voidDate, int voidedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var openItem = await db.ApOpenItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SourceType == OpenItemSourceType && i.SourceId == vendorCreditMemoId, ct);
        if (openItem is not null)
            openItem.Status = OpenItemStatus.Voided;

        var keys = new[] { IssueKey(vendorCreditMemoId), ReturnClearingKey(vendorCreditMemoId) };
        var entries = await db.JournalEntries
            .Where(e => e.BookId == book.Id && e.IdempotencyKey != null && keys.Contains(e.IdempotencyKey)
                && e.Status == JournalEntryStatus.Posted)
            .ToListAsync(ct);
        if (entries.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        foreach (var entry in entries)
        {
            var reversalDate = await IsHardClosedAsync(book.Id, entry.EntryDate, ct) ? voidDate : entry.EntryDate;
            await postingEngine.ReverseAsSystemAsync(entry.Id, reversalDate, $"Vendor credit memo {vendorCreditMemoId} voided", voidedByUserId, ct);
        }
    }

    public async Task PostApplicationAsync(int applicationId, int appliedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var app = await db.VendorCreditMemoApplications
            .Include(a => a.VendorCreditMemo)
            .Include(a => a.VendorBill)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || !await IssuePostedAsync(book.Id, app.VendorCreditMemoId, ct))
            return;

        var key = ApplicationKey(app.Id);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var memoSide = Money.Round(app.Amount * app.VendorCreditMemo.FxRate);
        var billSide = Money.Round(app.Amount * app.VendorBill.FxRate);

        await MoveOpenItemAsync(OpenItemSourceType, app.VendorCreditMemoId, app.Amount, memoSide, ct);
        await MoveOpenItemAsync("VendorBill", app.VendorBillId, app.Amount, billSide, ct);

        if (memoSide == billSide)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var memoNumber = app.VendorCreditMemo.MemoNumber;
        var billNumber = app.VendorBill.BillNumber;
        var lines = new List<PostingLine>(3)
        {
            new()
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = app.VendorBill.VendorId,
                Debit = billSide,
                Description = $"Bill {billNumber} settled by vendor memo {memoNumber}",
            },
            new()
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = app.VendorCreditMemo.VendorId,
                Credit = memoSide,
                Description = $"Vendor memo {memoNumber} applied to {billNumber}",
            },
        };
        AddFxPlug(lines, billSide - memoSide, $"vendor memo {memoNumber}");

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(app.AppliedAt.UtcDateTime),
            Source = JournalSource.AP,
            SourceType = "VendorCreditMemoApplication",
            SourceId = app.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Apply vendor memo {memoNumber} to bill {billNumber}",
            IdempotencyKey = key,
            Lines = lines,
        }, appliedByUserId, ct);
    }

    public async Task ReverseApplicationAsync(int applicationId, DateOnly reversalDate, int reversedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await FindBookAsync(ct);
        if (book is null)
            return;

        var app = await db.VendorCreditMemoApplications
            .Include(a => a.VendorCreditMemo)
            .Include(a => a.VendorBill)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || !await IssuePostedAsync(book.Id, app.VendorCreditMemoId, ct))
            return;

        await MoveOpenItemAsync(OpenItemSourceType, app.VendorCreditMemoId, -app.Amount, -Money.Round(app.Amount * app.VendorCreditMemo.FxRate), ct);
        await MoveOpenItemAsync("VendorBill", app.VendorBillId, -app.Amount, -Money.Round(app.Amount * app.VendorBill.FxRate), ct);

        var key = ApplicationKey(app.Id);
        var entry = await db.JournalEntries
            .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
        if (entry is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        await postingEngine.ReverseAsSystemAsync(
            entry.Id, reversalDate,
            $"Vendor memo {app.VendorCreditMemo.MemoNumber} unapplied from {app.VendorBill.BillNumber}", reversedByUserId, ct);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> PostReturnShippedAsync(
        int vendorReturnId, DateOnly shipDate, int userId, CancellationToken ct = default)
    {
        var relieved = new Dictionary<int, decimal>();
        if (!capabilities.IsEnabled(FullGlCapability))
            return relieved;

        var rtv = await db.VendorReturns
            .Include(r => r.Lines).ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(r => r.Id == vendorReturnId, ct);
        if (rtv is null)
            return relieved;

        var book = await RequireBookAsync(ct);
        var key = ReturnShipKey(rtv.Id);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return relieved;

        var lines = new List<PostingLine>();
        foreach (var line in rtv.Lines.OrderBy(l => l.LineNumber))
        {
            if (!IsStocked(line.Part))
                continue;
            var value = Money.Round(line.Quantity * line.UnitCost);
            if (value <= 0m)
                continue;

            if (valuation is not null
                && await db.InventoryValuations.AnyAsync(v => v.BookId == book.Id && v.PartId == line.PartId, ct))
                await valuation.ReverseReceiptAsync(book.Id, line.PartId, line.Quantity, value, ct);

            relieved[line.Id] = value;
            var description = $"Return to vendor {rtv.ReturnNumber} — {line.Part.PartNumber}"
                + (string.IsNullOrWhiteSpace(line.LotNumber) ? string.Empty : $" lot {line.LotNumber}")
                + $" x{line.Quantity:0.####}";
            lines.Add(new PostingLine { AccountKey = KeyVendorReturnClearing, Debit = value, Description = description });
            lines.Add(new PostingLine { AccountKey = InventoryKeyFor(line.Part), Credit = value, Description = description });
        }

        if (lines.Count == 0)
            return relieved;

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = shipDate,
            Source = JournalSource.Inventory,
            SourceType = "VendorReturn",
            SourceId = rtv.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Return to vendor {rtv.ReturnNumber} shipped",
            IdempotencyKey = key,
            Lines = lines,
        }, userId, ct);
        return relieved;
    }

    private static bool IsStocked(Part? part) => part?.InventoryClass is
        InventoryClass.Raw or InventoryClass.Component or InventoryClass.Subassembly or InventoryClass.FinishedGood;

    private static string InventoryKeyFor(Part part) => part.InventoryClass switch
    {
        InventoryClass.Subassembly => KeyInventorySubassembly,
        InventoryClass.FinishedGood => KeyInventoryFg,
        _ => KeyInventoryRaw,
    };

    private static void AddFxPlug(List<PostingLine> lines, decimal debitsOverCredits, string what)
    {
        if (debitsOverCredits > 0m)
            lines.Add(new PostingLine { AccountKey = KeyFxGain, Credit = debitsOverCredits, Description = $"Realized FX gain — {what}" });
        else if (debitsOverCredits < 0m)
            lines.Add(new PostingLine { AccountKey = KeyFxLoss, Debit = -debitsOverCredits, Description = $"Realized FX loss — {what}" });
    }

    private async Task MoveOpenItemAsync(string sourceType, int sourceId, decimal txn, decimal functional, CancellationToken ct)
    {
        var item = await db.ApOpenItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SourceType == sourceType && i.SourceId == sourceId, ct);
        if (item is null)
        {
            Log.Warning("AP open item missing for {SourceType} {SourceId}; vendor memo settlement left it untouched.", sourceType, sourceId);
            return;
        }

        item.AppliedTxnAmount = Math.Max(0m, item.AppliedTxnAmount + txn);
        item.AppliedFunctionalAmount = Math.Max(0m, item.AppliedFunctionalAmount + functional);
        item.RecomputeStatus();
    }

    private async Task<bool> IssuePostedAsync(int bookId, int memoId, CancellationToken ct)
    {
        var key = IssueKey(memoId);
        return await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == bookId && e.IdempotencyKey == key && e.Status == JournalEntryStatus.Posted, ct);
    }

    private Task<Book?> FindBookAsync(CancellationToken ct)
        => db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);

    private async Task<Book> RequireBookAsync(CancellationToken ct)
        => await FindBookAsync(ct)
           ?? throw new PostingException(
               "NO_POSTING_BOOK",
               "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the vendor memo into.");

    private async Task<bool> IsHardClosedAsync(int bookId, DateOnly date, CancellationToken ct)
        => await db.FiscalPeriods.AsNoTracking()
            .AnyAsync(p => p.FiscalYear.BookId == bookId
                && p.StartDate <= date && p.EndDate >= date
                && p.Status == FiscalPeriodStatus.HardClosed, ct);
}
