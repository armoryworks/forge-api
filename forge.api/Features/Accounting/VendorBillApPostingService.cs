using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services;
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
/// Phase-2 STAGE A — Vendor-bill / AP posting (ACCOUNTING_SUITE_PLAN §6 Phase-2, §7 matrix). The AP
/// counterpart of <see cref="InvoiceArPostingService"/>: when a <see cref="VendorBill"/> is approved and
/// CAP-ACCT-FULLGL is on, posts the bill journal <b>inline, in the operational command's transaction</b>:
/// <list type="bullet">
///   <item><b>Dr</b> each bill line's resolved expense/asset account (the line's
///         <c>AccountDeterminationKey</c>, default <c>OPERATING_EXPENSE</c>) for the line total.</item>
///   <item><b>Dr</b> <c>OPERATING_EXPENSE</c> for the bill's <c>TaxAmount</c> when present — purchase tax
///         expensed as non-recoverable (the common U.S. treatment; ratify per docs/accounting/PHASE2_STATUS.md).</item>
///   <item><b>Cr</b> <c>AP_CONTROL</c> for the bill <c>Total</c> — the vendor payable (party = vendor, §5.2).</item>
/// </list>
/// The entry balances by construction: Σ line totals + tax == Total.
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default)</b> — the gate is the first thing the
/// method does; with FULLGL off it returns immediately (no-op), so the operational bill flow is unchanged.
/// Standalone (non-PO) bills only in STAGE A; the PO-matched GRNI-clearing + PPV variant is STAGE D
/// (<c>VendorBill.PurchaseOrderId</c> is the seam). Idempotent via the engine's (BookId, IdempotencyKey)
/// de-dupe.</para>
/// </summary>
public interface IVendorBillApPostingService
{
    /// <summary>
    /// Posts the AP / expense journal for an approved vendor bill, when (and only when) CAP-ACCT-FULLGL is
    /// enabled. A no-op while the capability is off. Idempotent: a re-post returns the existing entry.
    /// </summary>
    Task PostVendorBillApprovedAsync(int vendorBillId, int approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Reverses the posted AP / expense journal for a bill being voided (posts an equal-and-opposite entry,
    /// flips the original to Reversed). A no-op while CAP-ACCT-FULLGL is off, or if nothing is currently
    /// posted for the bill (e.g. it was approved while the capability was off).
    /// </summary>
    Task ReverseVendorBillApprovedAsync(int vendorBillId, int reversedByUserId, CancellationToken ct = default);

    /// <summary>
    /// For a purchase order being closed or short-closed: releases the receipt freight accrual no freight bill
    /// has cleared (Dr FREIGHT_CLEARING / Cr FREIGHT_IN), so the variance lands in freight-in. A no-op while
    /// CAP-ACCT-FULLGL is off or when nothing is left. Idempotent per PO.
    /// </summary>
    Task PostPurchaseOrderFreightCloseAsync(int purchaseOrderId, DateOnly closeDate, int closedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VendorBillApPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IVendorBillApPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyApControl = "AP_CONTROL";
    // Purchase tax (non-recoverable) expensed to G&A. Ratify per docs/accounting/PHASE2_STATUS.md (use-tax vs recoverable).
    private const string KeyPurchaseTaxExpense = "OPERATING_EXPENSE";
    // STAGE-D 3-way match: a PO-linked bill clears GRNI (at PO price) instead of debiting expense, with the
    // bill-vs-PO price difference going to PPV.
    private const string KeyGrni = "GRNI";
    private const string KeyPpv = "PURCHASE_PRICE_VARIANCE";
    private const string GrniRateDifferenceSourceType = "VendorBillFx";
    private const string KeyFreightClearing = "FREIGHT_CLEARING";
    private const string KeyFreightIn = "FREIGHT_IN";

    public async Task PostVendorBillApprovedAsync(
        int vendorBillId, int approvedByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default): zero behavior change while FULLGL is off ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        await PostCoreAsync(vendorBillId, approvedByUserId, ct);
    }

    public async Task ReverseVendorBillApprovedAsync(
        int vendorBillId, int reversedByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default) ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        // The live posted entry for this bill (the ":BILL" entry). Skip if none is currently posted — the
        // bill may have been approved while FULLGL was off, or already reversed.
        var entry = await db.Set<JournalEntry>()
            .Where(e => e.Source == JournalSource.AP
                && e.SourceType == "VendorBill"
                && e.SourceId == vendorBillId
                && e.Status == JournalEntryStatus.Posted)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(ct);

        // A bill promoted from an expense BEFORE this build (reconstructed by the boot backfill)
        // carries its AP credit under the legacy AP:Expense:{id}:EXPENSE origination — the bill itself
        // never posted. Reverse that entry instead, so voiding the bill still zeroes both the expense
        // debit and the payable in the GL.
        if (entry is null)
        {
            var expenseId = await db.Set<VendorBill>()
                .Where(b => b.Id == vendorBillId)
                .Select(b => b.ExpenseId)
                .FirstOrDefaultAsync(ct);

            if (expenseId is not null)
            {
                entry = await db.Set<JournalEntry>()
                    .Where(e => e.Source == JournalSource.AP
                        && e.SourceType == "Expense"
                        && e.SourceId == expenseId
                        && e.Status == JournalEntryStatus.Posted)
                    .OrderByDescending(e => e.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (entry is null)
            return;

        // ── AP open item (AP-001): flip the bill's item to Voided — logically zeroing it. A Voided
        // item is excluded from aging AND the control reconciliation adds it to neither side,
        // matching the reversed GL (the bill's AP control nets to zero). Applied amounts are left
        // untouched for audit (VoidVendorBill blocks bills with payments applied, so they are 0).
        // Staged BEFORE ReverseAsync so the engine's SaveChangesAsync flushes both atomically.
        var openItem = await db.ApOpenItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.SourceType == "VendorBill" && i.SourceId == vendorBillId, ct);
        if (openItem is not null)
            openItem.Status = OpenItemStatus.Voided;

        // Reverse in the original entry's period (clean same-period correction). When period close lands
        // (Phase 3), a closed-period reversal becomes a policy choice the engine already guards.
        await postingEngine.ReverseAsSystemAsync(
            entry.Id, entry.EntryDate, $"Vendor bill {vendorBillId} voided", reversedByUserId, ct);

        var fxKey = GrniRateDifferenceKey(vendorBillId);
        var fxEntry = await db.Set<JournalEntry>()
            .Where(e => e.BookId == entry.BookId
                && e.IdempotencyKey == fxKey
                && e.Status == JournalEntryStatus.Posted
                && e.ReversedByEntryId == null)
            .FirstOrDefaultAsync(ct);
        if (fxEntry is not null)
            await postingEngine.ReverseAsSystemAsync(
                fxEntry.Id, fxEntry.EntryDate, $"Vendor bill {vendorBillId} voided", reversedByUserId, ct);
    }

    public async Task PostPurchaseOrderFreightCloseAsync(
        int purchaseOrderId, DateOnly closeDate, int closedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);
        if (book is null)
            return;

        var residual = await OpenFreightAccrualAsync(book.Id, purchaseOrderId, null, ct);
        if (residual <= 0m)
            return;

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = closeDate,
            Source = JournalSource.AP,
            SourceType = PurchaseOrderFreightCloseSourceType,
            SourceId = purchaseOrderId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"PO {purchaseOrderId} closed: freight accrued at receipt and never billed",
            IdempotencyKey = $"{JournalSource.AP}:PurchaseOrder:{purchaseOrderId}:FREIGHT_CLOSE",
            Lines =
            [
                new PostingLine { AccountKey = KeyFreightClearing, Debit = residual, Description = $"Freight clearing released — PO {purchaseOrderId}" },
                new PostingLine { AccountKey = KeyFreightIn, Credit = residual, Description = $"Freight accrual over billed freight — PO {purchaseOrderId}" },
            ],
        }, closedByUserId, ct);
    }

    private async Task PostCoreAsync(int vendorBillId, int approvedByUserId, CancellationToken ct)
    {
        var bill = await db.Set<VendorBill>()
            .Include(b => b.Lines).ThenInclude(l => l.PurchaseOrderLine)
            .FirstOrDefaultAsync(b => b.Id == vendorBillId, ct);

        if (bill is null)
        {
            Log.Warning(
                "Vendor-bill posting skipped: bill {VendorBillId} not found when posting (FULLGL on).",
                vendorBillId);
            return;
        }

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the vendor bill into.");

        var total = bill.Total;
        if (total <= 0m)
        {
            Log.Information(
                "Vendor-bill posting skipped: bill {VendorBillId} has non-positive total {Total}.",
                vendorBillId, total);
            return;
        }

        // EntryDate = the bill date in the book's reporting context (DateOnly is UTC-normalization-immune, §5.1).
        var entryDate = DateOnly.FromDateTime(bill.BillDate.UtcDateTime);

        var lines = new List<PostingLine>(bill.Lines.Count + 3);
        var tax = Money.Round(bill.TaxAmount);
        var itemLines = bill.Lines.Where(l => !l.IsFreight).ToList();
        var freightLines = bill.Lines.Where(l => l.IsFreight).OrderBy(l => l.LineNumber).ToList();
        var freightTotal = freightLines.Sum(l => Money.Round(l.LineTotal));

        // Debit side splits on whether the bill is matched to a purchase order (3-way match, STAGE D):
        //   • Standalone bill → Dr each line's resolved expense/asset account for the line total.
        //   • PO-matched bill → Dr GRNI at the PO price (clearing the accrual booked at receipt) and route
        //     the bill-vs-PO price difference to PPV. This is the third leg of the 3-way match (PO ↔ receipt
        //     ↔ bill); the receipt already debited inventory & credited GRNI (STAGE C).
        if (itemLines.Count > 0)
        {
            if (bill.PurchaseOrderId is not null)
                BuildPoMatchedDebits(bill, itemLines, lines, total - tax - freightTotal - itemLines.Sum(l => l.LineTotal));
            else
                BuildStandaloneDebits(itemLines, lines, total - tax - freightTotal);
        }

        if (freightLines.Count > 0)
            await BuildFreightDebitsAsync(bill, book.Id, freightLines, lines, itemLines.Count == 0 ? total - tax - freightTotal : 0m, ct);

        // Dr purchase tax (expensed) when the vendor charged tax.
        if (tax > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyPurchaseTaxExpense,
                Debit = tax,
                Description = $"Purchase tax — bill {bill.BillNumber}",
            });
        }

        // Cr AP control for the full payable (party = vendor → the AP sub-ledger).
        lines.Add(new PostingLine
        {
            AccountKey = KeyApControl,
            PartyType = SubledgerPartyType.Vendor,
            PartyId = bill.VendorId,
            Credit = total,
            Description = $"AP — vendor bill {bill.BillNumber}",
        });

        var idempotencyKey = $"{JournalSource.AP}:VendorBill:{bill.Id}:BILL";

        // ── AP open item (AP-001 open-item sub-ledger). The Cr AP_CONTROL above is the single seam
        // where this bill's payable enters the control account, so the per-document open item is
        // created HERE, in the same transaction (the engine's SaveChangesAsync flushes both
        // atomically) — that is what keeps Σ open items == control balance. Idempotency: the
        // engine's journal de-dupe can return early without saving, so this guards independently
        // on (SourceType, SourceId) (unique-indexed as the DB backstop).
        var hasOpenItem = await db.ApOpenItems.IgnoreQueryFilters()
            .AnyAsync(i => i.SourceType == "VendorBill" && i.SourceId == bill.Id, ct);
        if (!hasOpenItem)
        {
            db.ApOpenItems.Add(new ApOpenItem
            {
                BookId = book.Id,
                VendorId = bill.VendorId,
                SourceType = "VendorBill",
                SourceId = bill.Id,
                DocumentNumber = bill.BillNumber,
                DocumentDate = bill.BillDate,
                DueDate = bill.DueDate,
                CurrencyId = bill.CurrencyId,
                FxRate = bill.FxRate,
                // txn = the POSTED bill total in its currency; functional = txn × booking rate,
                // rounded exactly like the engine rounds the line.
                OriginalTxnAmount = total,
                OriginalFunctionalAmount = Math.Round(total * bill.FxRate, 2, MidpointRounding.AwayFromZero),
                Status = OpenItemStatus.Open,
            });
        }

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "VendorBill",
            SourceId = bill.Id,
            // Phase-4 FX: post the AP / expense entry in the bill's TRANSACTION currency at its BOOKING rate.
            // The engine multiplies each line's txn amount by FxRate → FunctionalAmount. Single-currency
            // (functional, FxRate 1) is byte-for-byte the prior behavior.
            CurrencyId = bill.CurrencyId,
            FxRate = bill.FxRate,
            Memo = $"Vendor bill {bill.BillNumber} approved"
                 + (bill.VendorInvoiceNumber is { Length: > 0 } vin ? $" (vendor inv {vin})" : string.Empty),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        var entry = await postingEngine.PostAsSystemAsync(request, approvedByUserId, ct);

        if (bill.PurchaseOrderId is not null)
            await PostGrniRateDifferenceAsync(bill, book.Id, book.FunctionalCurrencyId, entry, entryDate, approvedByUserId, ct);

        await TryAuditAsync(bill, entry, total, approvedByUserId, ct);
    }

    /// <summary>The idempotency key of the functional-currency entry that settles a PO-matched bill's GRNI rate difference.</summary>
    public static string GrniRateDifferenceKey(int vendorBillId) => $"{JournalSource.AP}:VendorBill:{vendorBillId}:FX_GRNI";

    /// <summary>
    /// A PO-matched bill clears GRNI at the bill's rate, while the receipt accrued it at the receipt's rate. This
    /// functional-currency entry moves the difference between the two to purchase price variance, so GRNI nets
    /// to zero for the billed goods: bill rate above the receipt rate posts Dr PPV / Cr GRNI, below posts
    /// Dr GRNI / Cr PPV. The receipt rate of a PO line is the quantity-weighted rate of its receipts. Nothing
    /// posts when the two agree, which is always the case for a single-currency bill.
    /// </summary>
    private async Task PostGrniRateDifferenceAsync(
        VendorBill bill, int bookId, int functionalCurrencyId, JournalEntry billEntry, DateOnly entryDate,
        int userId, CancellationToken ct)
    {
        var poLineIds = bill.Lines
            .Where(l => l.Quantity > 0m && l.PurchaseOrderLineId is not null)
            .Select(l => l.PurchaseOrderLineId!.Value)
            .Distinct()
            .ToList();
        if (poLineIds.Count == 0)
            return;

        var receipts = await db.Set<ReceivingRecord>().AsNoTracking()
            .Where(r => poLineIds.Contains(r.PurchaseOrderLineId) && r.QuantityReceived > 0m)
            .Select(r => new { r.PurchaseOrderLineId, r.QuantityReceived, r.FxRate })
            .ToListAsync(ct);
        var receiptRates = receipts
            .GroupBy(r => r.PurchaseOrderLineId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => r.QuantityReceived * r.FxRate) / g.Sum(r => r.QuantityReceived));

        if (bill.FxRate == 1m && receiptRates.Values.All(r => r == 1m))
            return;

        var grniAccountId = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == KeyGrni)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (grniAccountId is null)
            return;

        var clearedAtBillRate = billEntry.Lines
            .Where(l => l.GlAccountId == grniAccountId && l.Debit > 0m)
            .Sum(l => l.FunctionalAmount);

        var accruedAtReceiptRate = bill.Lines
            .Where(l => l.Quantity > 0m && l.PurchaseOrderLine is not null)
            .Sum(l => FxMath.ToFunctional(
                l.Quantity * l.PurchaseOrderLine!.UnitPrice,
                receiptRates.GetValueOrDefault(l.PurchaseOrderLine.Id, 1m)));

        var difference = clearedAtBillRate - accruedAtReceiptRate;
        if (difference == 0m)
            return;

        var lines = difference > 0m
            ? new List<PostingLine>
            {
                new() { AccountKey = KeyPpv, Debit = difference, Description = $"Exchange-rate difference on received goods — bill {bill.BillNumber}" },
                new() { AccountKey = KeyGrni, Credit = difference, Description = $"GRNI rate difference — bill {bill.BillNumber}" },
            }
            : new List<PostingLine>
            {
                new() { AccountKey = KeyGrni, Debit = -difference, Description = $"GRNI rate difference — bill {bill.BillNumber}" },
                new() { AccountKey = KeyPpv, Credit = -difference, Description = $"Exchange-rate difference on received goods — bill {bill.BillNumber}" },
            };

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = bookId,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = GrniRateDifferenceSourceType,
            SourceId = bill.Id,
            CurrencyId = functionalCurrencyId,
            Memo = $"Vendor bill {bill.BillNumber}: GRNI cleared at the bill rate vs the receipt rate",
            IdempotencyKey = GrniRateDifferenceKey(bill.Id),
            Lines = lines,
        }, userId, ct);
    }

    /// <summary>
    /// Standalone (non-PO) bill: Dr each line's expense/asset account for the line total rounded to the cent —
    /// the line's explicit GL account when one was chosen, else its determination key. The cents left between
    /// <paramref name="linesTotal"/> (the rounded bill total less the rounded tax) and the Σ of the rounded lines
    /// go on the largest line, as the AR posting does, so the debits equal the AP credit exactly.
    /// </summary>
    private static void BuildStandaloneDebits(IReadOnlyList<VendorBillLine> billLines, List<PostingLine> lines, decimal linesTotal)
    {
        var ordered = billLines.OrderBy(l => l.LineNumber).ToList();
        var amounts = ordered.Select(l => Money.Round(l.LineTotal)).ToArray();
        var residual = linesTotal - amounts.Sum();
        if (residual != 0m && amounts.Length > 0)
        {
            var largest = 0;
            for (var i = 1; i < amounts.Length; i++)
                if (amounts[i] > amounts[largest]) largest = i;
            amounts[largest] += residual;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var l = ordered[i];
            var amount = amounts[i];
            if (amount <= 0m)
                continue; // skip degenerate/zero lines rather than emit a 0-amount posting line
            lines.Add(new PostingLine
            {
                AccountKey = l.GlAccountId is null ? l.AccountDeterminationKey : null,
                GlAccountId = l.GlAccountId,
                JobId = l.JobId, // carry the job tag for job costing (e.g. promoted job-costed expenses)
                Debit = amount,
                Description = l.Description,
            });
        }
    }

    /// <summary>
    /// PO-matched bill (3-way match): per line, <b>Dr GRNI</b> at the <i>received</i> quantity × <i>PO</i>
    /// unit price (clearing the accrual the receipt credited), and accumulate the bill-vs-PO price difference
    /// (<c>LineTotal − grniClear</c>) into a single net <b>PPV</b> line — Dr when unfavorable (billed &gt; PO),
    /// Cr when favorable. The debit side therefore equals Σ line totals (Σ grniClear + net PPV), so adding tax
    /// and crediting AP for <c>Total</c> balances by construction. <c>Total</c> and the tax are rounded to the
    /// cent, so <paramref name="roundingDifference"/> (rounded total − rounded tax − raw line totals) also lands
    /// in PPV; GRNI still clears exactly the raw amount the receipt accrued.
    ///
    /// <para>A line may only clear GRNI it has actually received-but-not-yet-billed: if the billed quantity
    /// exceeds the PO line's <c>UnbilledReceivedQuantity</c>, posting throws <c>GRNI_INSUFFICIENT</c> rather
    /// than over-clearing the accrual (bill-before-receipt / over-bill). The operational
    /// <c>ApproveVendorBill</c> guards the same condition before the transaction and advances
    /// <c>BilledQuantity</c> after this read, so a second bill against the same receipt can't double-clear.</para>
    /// </summary>
    private static void BuildPoMatchedDebits(
        VendorBill bill, IReadOnlyList<VendorBillLine> billLines, List<PostingLine> lines, decimal roundingDifference)
    {
        // Cumulative over-bill guard FIRST — a PO line may clear only the GRNI it has received-but-not-yet-
        // billed, and several bill lines can hit the SAME PO line, so the check must sum per PO line (not
        // per bill line). This mirrors ApproveVendorBill's operational guard so the result is correct even if
        // PostAsync is invoked directly. Skip zero/negative-quantity (degenerate) lines.
        foreach (var g in billLines
                     .Where(l => l.Quantity > 0m && l.PurchaseOrderLineId is not null)
                     .GroupBy(l => l.PurchaseOrderLineId!.Value))
        {
            var poLine = g.First().PurchaseOrderLine
                ?? throw new PostingException(
                    "PO_LINE_MISSING",
                    $"Vendor bill {bill.BillNumber} is PO-matched but a line's purchase-order line was not loaded.");

            var billedQty = g.Sum(l => l.Quantity);
            if (billedQty > poLine.UnbilledReceivedQuantity)
                throw new PostingException(
                    "GRNI_INSUFFICIENT",
                    $"Vendor bill {bill.BillNumber} bills {billedQty} against PO line {poLine.Id}, "
                  + $"but only {poLine.UnbilledReceivedQuantity} is received-but-not-yet-billed (bill-before-receipt / over-bill).");
        }

        var netPpv = roundingDifference;
        foreach (var l in billLines.OrderBy(l => l.LineNumber))
        {
            // Skip only truly degenerate (zero/negative-quantity) lines. A *zero-priced* line with a
            // positive quantity must still be processed: it clears the GRNI accrued for those received goods
            // (Dr GRNI at PO price) and books the full PO price as a favorable variance (Cr PPV), netting
            // Cr AP 0 for the line. Skipping it would strand the GRNI credit forever, since ApproveVendorBill
            // still advances BilledQuantity for the line (it sums quantity regardless of price).
            if (l.Quantity <= 0m)
                continue;

            var billedAmt = l.LineTotal;

            var poLine = l.PurchaseOrderLine
                ?? throw new PostingException(
                    "PO_LINE_MISSING",
                    $"Vendor bill {bill.BillNumber} line {l.LineNumber} is PO-matched but its purchase-order line was not loaded.");

            var grniClear = l.Quantity * poLine.UnitPrice;
            if (grniClear > 0m)
            {
                lines.Add(new PostingLine
                {
                    AccountKey = KeyGrni,
                    Debit = grniClear,
                    Description = $"GRNI clearing — bill {bill.BillNumber} line {l.LineNumber}",
                });
            }

            netPpv += billedAmt - grniClear;
        }

        if (netPpv > 0m)
        {
            // Billed above PO price → unfavorable variance (an added cost) → Dr PPV.
            lines.Add(new PostingLine
            {
                AccountKey = KeyPpv,
                Debit = netPpv,
                Description = $"Purchase price variance (unfavorable) — bill {bill.BillNumber}",
            });
        }
        else if (netPpv < 0m)
        {
            // Billed below PO price → favorable variance (a cost recovery) → Cr PPV.
            lines.Add(new PostingLine
            {
                AccountKey = KeyPpv,
                Credit = -netPpv,
                Description = $"Purchase price variance (favorable) — bill {bill.BillNumber}",
            });
        }
    }

    /// <summary>
    /// Freight lines. A line for a purchase order debits FREIGHT_CLEARING up to that PO's open freight
    /// accrual (<see cref="OpenFreightAccrualAsync"/>, less what earlier lines of this bill cleared) and
    /// FREIGHT_IN for any excess; a bill below the accrual leaves the rest for later freight bills. The amount
    /// cleared is kept on the line (<see cref="VendorBillLine.FreightClearedAmount"/>, functional) so the next
    /// bill clears only what is left. A line with no PO debits its own account or key (FREIGHT_IN by default,
    /// FREIGHT_OUT for an outbound carrier bill). <paramref name="roundingDifference"/> (cents between the
    /// rounded bill and its rounded lines when the bill has only freight) goes on the last debit that is not a
    /// clearing debit, or on the last clearing debit (and its cleared amount) when every debit clears.
    /// </summary>
    private async Task BuildFreightDebitsAsync(
        VendorBill bill, int bookId, IReadOnlyList<VendorBillLine> freightLines, List<PostingLine> lines,
        decimal roundingDifference, CancellationToken ct)
    {
        var clearedHere = new Dictionary<int, decimal>();
        var clearingLines = new Dictionary<int, VendorBillLine>();
        var first = lines.Count;
        await LockFreightPurchaseOrdersAsync(freightLines, ct);
        foreach (var l in freightLines)
        {
            var amount = Money.Round(l.LineTotal);
            l.FreightClearedAmount = null;
            if (amount <= 0m)
                continue;

            if (l.FreightForPurchaseOrderId is not int poId)
            {
                lines.Add(new PostingLine
                {
                    AccountKey = l.GlAccountId is null ? l.AccountDeterminationKey : null,
                    GlAccountId = l.GlAccountId,
                    JobId = l.JobId,
                    Debit = amount,
                    Description = $"Freight — bill {bill.BillNumber} line {l.LineNumber}: {l.Description}",
                });
                continue;
            }

            var open = await OpenFreightAccrualAsync(bookId, poId, bill.Id, ct) - clearedHere.GetValueOrDefault(poId);
            var clearTxn = open <= 0m ? 0m
                : Math.Min(amount, Money.Round(bill.FxRate == 1m ? open : open / bill.FxRate));
            if (clearTxn > 0m)
            {
                var clearedFunctional = FxMath.ToFunctional(clearTxn, bill.FxRate);
                l.FreightClearedAmount = clearedFunctional;
                clearedHere[poId] = clearedHere.GetValueOrDefault(poId) + clearedFunctional;
                clearingLines[lines.Count] = l;
                lines.Add(new PostingLine
                {
                    AccountKey = KeyFreightClearing,
                    Debit = clearTxn,
                    Description = $"Freight clearing — bill {bill.BillNumber} line {l.LineNumber} (PO {poId})",
                });
            }

            if (amount - clearTxn > 0m)
                lines.Add(new PostingLine
                {
                    AccountKey = KeyFreightIn,
                    Debit = amount - clearTxn,
                    Description = $"Freight-in — bill {bill.BillNumber} line {l.LineNumber}: {l.Description}",
                });
        }

        if (roundingDifference != 0m && lines.Count > first)
        {
            var target = lines.Count - 1;
            for (var i = lines.Count - 1; i >= first; i--)
            {
                if (!clearingLines.ContainsKey(i))
                {
                    target = i;
                    break;
                }
            }

            var last = lines[target];
            lines[target] = new PostingLine
            {
                AccountKey = last.AccountKey,
                GlAccountId = last.GlAccountId,
                JobId = last.JobId,
                Debit = last.Debit + roundingDifference,
                Description = last.Description,
            };
            if (clearingLines.TryGetValue(target, out var clearingLine))
                clearingLine.FreightClearedAmount = FxMath.ToFunctional(last.Debit + roundingDifference, bill.FxRate);
        }
    }

    /// <summary>
    /// Locks the purchase orders whose freight these lines clear, so two bills (or a bill and a PO close) can't
    /// both clear the same accrual. Postgres only; a no-op on other providers.
    /// </summary>
    private async Task LockFreightPurchaseOrdersAsync(IReadOnlyList<VendorBillLine> freightLines, CancellationToken ct)
    {
        var poIds = freightLines
            .Select(l => l.FreightForPurchaseOrderId)
            .OfType<int>()
            .Distinct()
            .Order()
            .ToArray();
        if (poIds.Length == 0 || !db.Database.IsNpgsql())
            return;

        await db.Database.ExecuteSqlRawAsync(
            "SELECT id FROM purchase_orders WHERE id = ANY({0}) ORDER BY id FOR UPDATE", [poIds], ct);
    }

    /// <summary>
    /// The freight a PO's receipts accrued in FREIGHT_CLEARING and nothing has cleared yet, in the functional
    /// currency: the credits of its posted receipt entries, less what freight lines on other bills that are not
    /// void cleared, less what closing the PO released (<see cref="PurchaseOrderFreightCloseSourceType"/>).
    /// </summary>
    public async Task<decimal> OpenFreightAccrualAsync(int bookId, int purchaseOrderId, int? excludingBillId, CancellationToken ct)
    {
        var clearingAccounts = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == KeyFreightClearing)
            .Select(r => r.GlAccountId)
            .ToListAsync(ct);
        if (clearingAccounts.Count == 0)
            return 0m;

        var accrued = await db.Set<JournalLine>().AsNoTracking()
            .Where(l => l.BookId == bookId && clearingAccounts.Contains(l.GlAccountId) && l.Credit > 0m
                && l.JournalEntry.Status == JournalEntryStatus.Posted
                && l.JournalEntry.SourceType == "Receipt" && l.JournalEntry.SourceId == purchaseOrderId)
            .SumAsync(l => (decimal?)l.FunctionalAmount, ct) ?? 0m;

        var released = await db.Set<JournalLine>().AsNoTracking()
            .Where(l => l.BookId == bookId && clearingAccounts.Contains(l.GlAccountId) && l.Debit > 0m
                && l.JournalEntry.Status == JournalEntryStatus.Posted
                && l.JournalEntry.SourceType == PurchaseOrderFreightCloseSourceType && l.JournalEntry.SourceId == purchaseOrderId)
            .SumAsync(l => (decimal?)l.FunctionalAmount, ct) ?? 0m;

        var cleared = await db.Set<VendorBillLine>().AsNoTracking()
            .Where(l => l.FreightForPurchaseOrderId == purchaseOrderId && l.FreightClearedAmount != null
                && l.VendorBill.Status != VendorBillStatus.Void && l.VendorBill.Status != VendorBillStatus.Draft
                && (excludingBillId == null || l.VendorBillId != excludingBillId))
            .SumAsync(l => (decimal?)l.FreightClearedAmount, ct) ?? 0m;

        return accrued - released - cleared;
    }

    /// <summary>Source type of the entry that releases a closed PO's leftover freight accrual to FREIGHT_IN.</summary>
    public const string PurchaseOrderFreightCloseSourceType = "PurchaseOrderFreight";

    private async Task TryAuditAsync(
        VendorBill bill, JournalEntry entry, decimal total, int actorUserId, CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null,
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    source = entry.Source.ToString(),
                    entryDate = entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    vendorId = bill.VendorId,
                    total,
                },
                reason = $"Vendor bill {bill.BillNumber} approved — AP / expense posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlVendorBillPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Vendor-bill posting audit write failed for bill {VendorBillId} (entry {EntryId}); posting is committed.",
                bill.Id, entry.Id);
        }
    }
}
