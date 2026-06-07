using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
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
///         expensed as non-recoverable (the common U.S. treatment; ratify per PHASE2_STATUS).</item>
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
    // Purchase tax (non-recoverable) expensed to G&A. Ratify per PHASE2_STATUS (use-tax vs recoverable).
    private const string KeyPurchaseTaxExpense = "OPERATING_EXPENSE";
    // STAGE-D 3-way match: a PO-linked bill clears GRNI (at PO price) instead of debiting expense, with the
    // bill-vs-PO price difference going to PPV.
    private const string KeyGrni = "GRNI";
    private const string KeyPpv = "PURCHASE_PRICE_VARIANCE";

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

        if (entry is null)
            return;

        // Reverse in the original entry's period (clean same-period correction). When period close lands
        // (Phase 3), a closed-period reversal becomes a policy choice the engine already guards.
        await postingEngine.ReverseAsync(
            entry.Id, entry.EntryDate, $"Vendor bill {vendorBillId} voided", reversedByUserId, ct);
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

        var total = bill.Total; // Σ line totals + tax
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

        // Debit side splits on whether the bill is matched to a purchase order (3-way match, STAGE D):
        //   • Standalone bill → Dr each line's resolved expense/asset account for the line total.
        //   • PO-matched bill → Dr GRNI at the PO price (clearing the accrual booked at receipt) and route
        //     the bill-vs-PO price difference to PPV. This is the third leg of the 3-way match (PO ↔ receipt
        //     ↔ bill); the receipt already debited inventory & credited GRNI (STAGE C).
        if (bill.PurchaseOrderId is not null)
            BuildPoMatchedDebits(bill, lines);
        else
            BuildStandaloneDebits(bill, lines);

        // Dr purchase tax (expensed) when the vendor charged tax.
        if (bill.TaxAmount > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyPurchaseTaxExpense,
                Debit = bill.TaxAmount,
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

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "VendorBill",
            SourceId = bill.Id,
            CurrencyId = book.FunctionalCurrencyId, // Phase-1/2 single-currency invariant
            Memo = $"Vendor bill {bill.BillNumber} approved"
                 + (bill.VendorInvoiceNumber is { Length: > 0 } vin ? $" (vendor inv {vin})" : string.Empty),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        var entry = await postingEngine.PostAsync(request, approvedByUserId, ct);

        await TryAuditAsync(bill, entry, total, approvedByUserId, ct);
    }

    /// <summary>
    /// Standalone (non-PO) bill: Dr each line's resolved expense/asset account for the line total.
    /// </summary>
    private static void BuildStandaloneDebits(VendorBill bill, List<PostingLine> lines)
    {
        foreach (var l in bill.Lines.OrderBy(l => l.LineNumber))
        {
            var amount = l.LineTotal;
            if (amount <= 0m)
                continue; // skip degenerate/zero lines rather than emit a 0-amount posting line
            lines.Add(new PostingLine
            {
                AccountKey = l.AccountDeterminationKey,
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
    /// and crediting AP for <c>Total</c> balances by construction.
    ///
    /// <para>A line may only clear GRNI it has actually received-but-not-yet-billed: if the billed quantity
    /// exceeds the PO line's <c>UnbilledReceivedQuantity</c>, posting throws <c>GRNI_INSUFFICIENT</c> rather
    /// than over-clearing the accrual (bill-before-receipt / over-bill). The operational
    /// <c>ApproveVendorBill</c> guards the same condition before the transaction and advances
    /// <c>BilledQuantity</c> after this read, so a second bill against the same receipt can't double-clear.</para>
    /// </summary>
    private static void BuildPoMatchedDebits(VendorBill bill, List<PostingLine> lines)
    {
        // Cumulative over-bill guard FIRST — a PO line may clear only the GRNI it has received-but-not-yet-
        // billed, and several bill lines can hit the SAME PO line, so the check must sum per PO line (not
        // per bill line). This mirrors ApproveVendorBill's operational guard so the result is correct even if
        // PostAsync is invoked directly. Skip zero/negative-quantity (degenerate) lines.
        foreach (var g in bill.Lines
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

        var netPpv = 0m;
        foreach (var l in bill.Lines.OrderBy(l => l.LineNumber))
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
