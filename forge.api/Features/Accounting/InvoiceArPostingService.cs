using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services;
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
/// Phase-1 STAGE A — AR / revenue / tax posting (ACCOUNTING_SUITE_PLAN §7 row 1
/// + 2, §8.4). When an invoice is finalized, this service posts the AR sub-ledger
/// journal entry <b>inline, in the operational command's transaction</b> (the
/// locked inline model — §2, §7): it adds the entry to the shared request-scoped
/// <see cref="AppDbContext"/> and the engine's <c>SaveChangesAsync</c> joins the
/// caller's unit of work.
///
/// <para><b>Posting (per §7 / matrix row 1–2):</b>
/// <list type="bullet">
///   <item>Dr AR_CONTROL (the customer's receivable, party = the customer)</item>
///   <item>Cr SALES_REVENUE per line — when control has transferred (delivered)</item>
///   <item>Cr DEFERRED_REVENUE per line — when the invoice precedes delivery
///         (PointInTime rev-rec); reclassified to revenue on delivery (TODO below)</item>
///   <item>Cr SALES_TAX_PAYABLE for the invoice tax (skipped for tax-exempt customers)</item>
/// </list>
/// </para>
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default).</b> The first
/// thing <see cref="PostInvoiceFinalizedAsync"/> does is gate on the capability
/// snapshot; with FULLGL off it returns immediately as a no-op — zero behavior
/// change to the operational invoice flow. Only when FULLGL is enabled does it
/// resolve the book and post. The call is additionally wrapped so that — while
/// dark — no posting path can throw into the operational action; once FULLGL is
/// ON a posting failure propagates and fails the operation (the inline model's
/// "fail visibly" rule — §2).</para>
///
/// <para><b>DEFER to Phase 2 — COGS / inventory relief.</b> The matrix's
/// "+ Dr COGS / Cr Finished-Goods for stocked goods" leg is NOT posted here: it
/// needs the per-part valuation store that Phase 2 introduces (§6, §8.1) and the
/// FG-not-yet-loaded edge (§12) must be resolved first. See the TODO at the call
/// site.</para>
/// </summary>
public interface IInvoiceArPostingService
{
    /// <summary>
    /// Posts the AR / revenue / tax journal for a finalized invoice, when (and
    /// only when) CAP-ACCT-FULLGL is enabled. A no-op while the capability is
    /// off. Idempotent: a re-finalize of the same invoice returns the existing
    /// entry via the engine's <c>(BookId, IdempotencyKey)</c> de-dupe.
    /// </summary>
    /// <param name="invoiceId">The invoice being finalized.</param>
    /// <param name="finalizedByUserId">Server-trusted actor (recorded as PostedBy + audit actor).</param>
    Task PostInvoiceFinalizedAsync(int invoiceId, int finalizedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class InvoiceArPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IInvoiceArPostingService
{
    // The capability that gates the whole GL. Must match CapabilityCatalog.
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    // Determination keys (must match SeedData.Accounting.cs). Business events
    // never hardcode account numbers — they resolve a (BookId, Key) rule (§5.1).
    private const string KeyArControl = "AR_CONTROL";
    private const string KeySalesRevenue = "SALES_REVENUE";
    private const string KeyDeferredRevenue = "DEFERRED_REVENUE";
    private const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";

    public async Task PostInvoiceFinalizedAsync(
        int invoiceId, int finalizedByUserId, CancellationToken ct = default)
    {
        // ── GATE 1 (dark by default): zero behavior change while FULLGL is off ──
        // This is the single most important guard. With CAP-ACCT-FULLGL OFF
        // (the default) the method returns before touching the engine or the
        // acct_* tables, so the operational invoice flow is byte-for-byte
        // unchanged and the existing invoice tests pass unmodified.
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        // From here on FULLGL is ON. A posting failure SHOULD fail the operation
        // visibly (inline model, §2) — so once we are past the dark gate we let
        // PostingException propagate. We only defensively guard the OFF path
        // (handled above) so a dark boot can never be perturbed.
        await PostCoreAsync(invoiceId, finalizedByUserId, ct);
    }

    private async Task PostCoreAsync(int invoiceId, int finalizedByUserId, CancellationToken ct)
    {
        // Load the invoice with everything the journal needs from the SHARED
        // request-scoped context (so the posting joins the caller's transaction).
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.Shipment)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null)
        {
            // The operational handler already validated existence; if it's gone
            // here something is wrong, but don't invent a ledger entry.
            Log.Warning(
                "AR posting skipped: invoice {InvoiceId} not found when finalizing (FULLGL on).",
                invoiceId);
            return;
        }

        // ── Resolve the single seeded posting book (single-entity for now, §5.1).
        // Multi-entity routing is a later concern; today there is one active Book.
        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (book is null)
        {
            // FULLGL is on but no book is seeded — a real misconfiguration.
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post AR into.");
        }

        // EntryDate = the invoice date in the book's reporting context. DateOnly
        // is immune to UTC normalization (§5.1); we take the date component of
        // the invoice's own InvoiceDate so the entry lands in the right period.
        var entryDate = DateOnly.FromDateTime(invoice.InvoiceDate.UtcDateTime);

        // Per-line money. Subtotal/Tax/Total mirror the Invoice entity's own
        // computed accessors so the journal ties out to the document exactly.
        var subtotal = invoice.Lines.Sum(l => l.LineTotal);

        // Sales tax accrues to SALES_TAX_PAYABLE — UNLESS the customer is
        // tax-exempt (resellers / gov / non-profit), which suppresses the tax
        // line on the document and therefore in the ledger too.
        var taxAmount = invoice.Customer.IsTaxExempt ? 0m : subtotal * invoice.TaxRate;

        // ── Revenue recognition (§8.4). Default PointInTime = recognize at
        // control transfer. If the invoice is finalized BEFORE delivery we book
        // to DEFERRED_REVENUE and reclassify on delivery; if control has already
        // transferred we book straight to SALES_REVENUE. Over-time methods
        // (PercentOfCompletion / Milestone) are not built yet — they fall back
        // to the same point-in-time behavior here and are a later-phase strategy
        // layer (§8.4).
        var controlTransferred = HasControlTransferred(invoice);
        var revenueKey = book.RevenueRecognitionMethod == RevenueRecognitionMethod.PointInTime && !controlTransferred
            ? KeyDeferredRevenue
            : KeySalesRevenue;

        var lines = new List<PostingLine>(invoice.Lines.Count + 2);

        // Dr AR for the full invoice total (subtotal + tax). AR is a control
        // account → the engine requires the customer party on this line (§5.2).
        var arTotal = subtotal + taxAmount;
        if (arTotal <= 0m)
        {
            // A zero/negative-total invoice (e.g. a fully-discounted or empty
            // finalize) has nothing to post; skip rather than emit a degenerate
            // entry the balanced-check would reject anyway.
            Log.Information(
                "AR posting skipped: invoice {InvoiceId} has non-positive postable total {Total}.",
                invoiceId, arTotal);
            return;
        }

        lines.Add(new PostingLine
        {
            AccountKey = KeyArControl,
            PartyType = SubledgerPartyType.Customer,
            PartyId = invoice.CustomerId,
            Debit = arTotal,
            Description = $"AR — invoice {invoice.InvoiceNumber}",
        });

        // Cr Revenue (or Deferred Revenue) per line, so the sub-ledger carries
        // line-level detail (and a later reclass can target individual lines).
        var lineNumber = 0;
        foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
        {
            lineNumber++;
            var amount = line.LineTotal;
            if (amount == 0m)
                continue; // a 0-value line contributes nothing to the credit side

            lines.Add(new PostingLine
            {
                AccountKey = revenueKey,
                Credit = amount,
                Description = $"{(revenueKey == KeyDeferredRevenue ? "Deferred revenue" : "Revenue")} — "
                            + $"invoice {invoice.InvoiceNumber} line {line.LineNumber}: {line.Description}",
            });
        }

        // Cr Sales-Tax-Payable for the (non-exempt) invoice tax. Jurisdiction
        // breakdown has no structural home yet (§12 Phase-1 deferral) — Phase 1
        // accrues to the single SALES_TAX_PAYABLE control; remittance-by-
        // jurisdiction is specified before tax remittance posting.
        if (taxAmount > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeySalesTaxPayable,
                Credit = taxAmount,
                Description = $"Sales tax payable — invoice {invoice.InvoiceNumber}",
            });
        }

        // ── Idempotency key (§5.2): source:type:id:purpose. AR/REVENUE for an
        // invoice; a re-finalize returns the existing entry (no throw, no dup).
        var idempotencyKey = $"{JournalSource.AR}:Invoice:{invoice.Id}:REVENUE";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AR,
            SourceType = "Invoice",
            SourceId = invoice.Id,
            CurrencyId = book.FunctionalCurrencyId, // Phase-0/1 single-currency invariant
            Memo = $"AR revenue recognition — invoice {invoice.InvoiceNumber}"
                 + (revenueKey == KeyDeferredRevenue ? " (deferred — invoice precedes delivery)" : string.Empty),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        // ── Post inline on the shared context. The engine validates, assigns the
        // EntryNumber, maintains LedgerBalance, and calls SaveChangesAsync so the
        // write participates in the operational command's transaction (§2, §5.2).
        var entry = await postingEngine.PostAsync(request, finalizedByUserId, ct);

        // ── Audit (§5.8): actor + before/after + reason on post. Best-effort —
        // an audit hiccup must not unwind a successful, committed posting.
        await TryAuditAsync(invoice, entry, revenueKey, arTotal, taxAmount, finalizedByUserId, ct);

        // ── TODO (Phase 2 — COGS / inventory relief, §6 / §7 matrix row 1 / §8.1 / §12):
        // For stocked finished-goods lines, control transfer should ALSO relieve
        // inventory: Dr COGS / Cr Finished-Goods at the per-unit valued cost.
        // That leg is intentionally NOT posted in Phase 1 because:
        //   (a) it needs the per-part valuation store (moving-average / FIFO
        //       layers / standard cost) that Phase 2 introduces (§8.1), and
        //   (b) the FG-not-yet-loaded edge (§12) must be resolved first — a
        //       make-to-order good can be sold before its FG balance is loaded,
        //       which would drive FG negative.
        // Leaving COGS unposted in Phase 1 means the interim P&L shows revenue
        // without matched COGS; CAP-RPT-FINANCIALS stays OFF until COGS is live
        // (§6 sequencing note). Do NOT post COGS here.

        // ── TODO (Phase 1 — deferred-revenue reclass on delivery, §8.4 / matrix
        // row 2): when the invoice was booked to DEFERRED_REVENUE (invoice
        // precedes delivery), a delivery trigger must reclassify it to
        // SALES_REVENUE: Dr DEFERRED_REVENUE / Cr SALES_REVENUE per line. The
        // intended trigger is the ShipmentDelivered transition (Shipment.Status
        // → Delivered / DeliveredDate set). That handler is not wired in STAGE A;
        // documented here as the reclass site (idempotency purpose = REVENUE_RECLASS).
    }

    /// <summary>
    /// Control transfer (§8.4 PointInTime): true when the goods/services tied to
    /// this invoice have been delivered. Detected via the linked
    /// <see cref="Shipment"/> (Status = Delivered or a DeliveredDate). When the
    /// invoice has no shipment link (pure service / job invoice) we treat
    /// finalize itself as the control-transfer event (one-shot completion), so
    /// revenue is recognized immediately rather than parked in deferred forever.
    /// </summary>
    private static bool HasControlTransferred(Invoice invoice)
    {
        if (invoice.ShipmentId is null || invoice.Shipment is null)
            return true; // no physical-goods shipment to wait on → recognize now

        return invoice.Shipment.Status == ShipmentStatus.Delivered
            || invoice.Shipment.DeliveredDate is not null;
    }

    private async Task TryAuditAsync(
        Invoice invoice,
        JournalEntry entry,
        string revenueKey,
        decimal arTotal,
        decimal taxAmount,
        int actorUserId,
        CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null, // an invoice finalize creates the entry; no prior ledger state
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    source = entry.Source.ToString(),
                    entryDate = entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    arTotal,
                    taxAmount,
                    revenueAccountKey = revenueKey,
                },
                reason = $"Invoice {invoice.InvoiceNumber} finalized — AR revenue recognition posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlInvoiceArPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null, // JournalEntry uses a long id; the long lives in details
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Audit is best-effort observability, never load-bearing for the
            // ledger. A failed audit write must not unwind the committed posting.
            Log.Warning(ex,
                "AR posting audit write failed for invoice {InvoiceId} (entry {EntryId}); posting itself is committed.",
                invoice.Id, entry.Id);
        }
    }
}
