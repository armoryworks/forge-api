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
/// <para><b>Phase-2 STAGE B — COGS / finished-goods relief.</b> On control transfer, stocked
/// finished-goods lines also relieve inventory: Dr COGS / Cr INVENTORY_FG at the resolved standard
/// cost, posted as a separate journal entry (idempotency purpose <c>:COGS</c>). Lines with no part, a
/// non-finished-goods part, or no resolvable cost are skipped. When the invoice precedes delivery
/// (deferred revenue), COGS waits for the same delivery trigger as the revenue reclass.</para>
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
    ISystemAuditWriter? auditWriter = null,
    // STAGE E — when wired, finished-goods relief is valued at the perpetual weighted-average and the store is
    // decremented; null (isolated tests) or no store row falls back to the part's standard cost.
    IInventoryValuationService? valuation = null) : IInvoiceArPostingService
{
    // The capability that gates the whole GL. Must match CapabilityCatalog.
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    // Determination keys (must match SeedData.Accounting.cs). Business events
    // never hardcode account numbers — they resolve a (BookId, Key) rule (§5.1).
    private const string KeyArControl = "AR_CONTROL";
    private const string KeySalesRevenue = "SALES_REVENUE";
    private const string KeyDeferredRevenue = "DEFERRED_REVENUE";
    private const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";

    // Phase-2 STAGE B — COGS / finished-goods relief at the sale.
    private const string KeyCogs = "COGS";
    private const string KeyInventoryFg = "INVENTORY_FG";

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
            .Include(i => i.Lines).ThenInclude(l => l.Part).ThenInclude(p => p!.CurrentCostCalculation)
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
            // entry the balanced-check would reject anyway. NOTE: this also skips
            // COGS/FG relief for a zero-revenue delivered FG line (free sample /
            // 100%-discount) — such goods relieve inventory via the shipment path,
            // not the invoice (documented limitation).
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

        // ── COGS / finished-goods relief (Phase-2 STAGE B, §7 matrix row 1). On control transfer,
        // relieve finished-goods inventory at standard cost for stocked FG lines: Dr COGS /
        // Cr INVENTORY_FG, as a SEPARATE journal entry (idempotency purpose :COGS) so it de-dupes
        // independently. When the invoice precedes delivery (!controlTransferred → deferred revenue),
        // inventory is NOT relieved yet — COGS waits for the same delivery trigger as the revenue
        // reclass (TODO below).
        if (controlTransferred)
            await PostCogsAsync(invoice, book, finalizedByUserId, ct);

        // ── TODO (Phase 1 — deferred-revenue reclass on delivery, §8.4 / matrix
        // row 2): when the invoice was booked to DEFERRED_REVENUE (invoice
        // precedes delivery), a delivery trigger must reclassify it to
        // SALES_REVENUE: Dr DEFERRED_REVENUE / Cr SALES_REVENUE per line. The
        // intended trigger is the ShipmentDelivered transition (Shipment.Status
        // → Delivered / DeliveredDate set). That handler is not wired in STAGE A;
        // documented here as the reclass site (idempotency purpose = REVENUE_RECLASS).
    }

    /// <summary>
    /// Phase-2 STAGE B — COGS / finished-goods relief at the sale (§7 matrix row 1, §8.1). For each
    /// stocked finished-goods line with a resolvable standard cost, posts Dr COGS for the line cost and
    /// a single aggregate Cr INVENTORY_FG, as a separate journal entry (idempotency purpose :COGS).
    /// Lines with no part, a non-finished-goods part, or no resolvable cost are skipped (the document
    /// still finalizes; gross margin understates for that line until a cost lands). INVENTORY_FG is an
    /// inventory control account reconciled by part via the valuation store (§8.1), so the credit posts
    /// party-less (the engine requires a party only for AR/AP control accounts).
    /// </summary>
    private async Task PostCogsAsync(Invoice invoice, Book book, int userId, CancellationToken ct)
    {
        var idempotencyKey = $"{JournalSource.Inventory}:Invoice:{invoice.Id}:COGS";

        // Idempotency guard for the valuation-store side-effect. The engine de-dupes the journal entry by
        // (BookId, IdempotencyKey), but ApplyIssueAsync (below) relieves the store OUTSIDE the engine — so a
        // re-finalize would double-relieve. Bail before touching the store if this COGS entry already exists.
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);
        if (alreadyPosted)
            return;

        var lines = new List<PostingLine>();
        decimal totalCogs = 0m;

        foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
        {
            if (!LineRelievesFinishedGoods(line))
                continue;

            // Perpetual costing (STAGE E): when the valuation store carries this FG part, relieve it at the
            // weighted-average and post that actual value — the store decrements in lock-step with the GL.
            // Otherwise fall back to the part's standard cost (and leave the store untouched).
            decimal lineCogs;
            var hasStoreRow = valuation is not null
                && await db.InventoryValuations.AnyAsync(v => v.BookId == book.Id && v.PartId == line.PartId, ct);
            if (hasStoreRow)
            {
                lineCogs = await valuation!.ApplyIssueAsync(book.Id, line.PartId!.Value, line.Quantity, ct);
            }
            else
            {
                var unitCost = ResolveStandardCost(line.Part!);
                if (unitCost is not { } cost || cost <= 0m)
                {
                    // No resolvable / non-positive standard cost — skip this line's COGS (don't block the
                    // sale). Logged so the partial-COGS gap is observable (gross margin understates here).
                    Log.Information(
                        "COGS skipped: invoice {InvoiceId} line {LineNumber} part {PartNumber} has no resolvable standard cost.",
                        invoice.Id, line.LineNumber, line.Part!.PartNumber);
                    continue;
                }
                lineCogs = cost * line.Quantity;
            }

            if (lineCogs <= 0m)
                continue;

            totalCogs += lineCogs;
            lines.Add(new PostingLine
            {
                AccountKey = KeyCogs,
                Debit = lineCogs,
                Description = $"COGS — invoice {invoice.InvoiceNumber} line {line.LineNumber}: {line.Description}",
            });
        }

        if (totalCogs <= 0m)
            return; // no stocked finished-goods lines with a cost → nothing to relieve

        // §12 (FG-not-yet-loaded edge) — when the valuation store carries a part, the relief above used its
        // weighted-average (STAGE E) and decremented it in lock-step. Parts NOT yet in the store fall back to
        // standard cost: a sale still relieves FG even if the inbound FG-load (production receipt / opening-
        // balance conversion §7A) hasn't run, which can drive INVENTORY_FG negative. Mitigated by loading
        // opening FG balances at go-live (§7A) before COGS is enabled.
        //
        // Cr INVENTORY_FG for the aggregate relief (inventory control account; party-less — reconciled
        // by part via the valuation store, §8.1).
        lines.Add(new PostingLine
        {
            AccountKey = KeyInventoryFg,
            Credit = totalCogs,
            Description = $"Finished-goods relief — invoice {invoice.InvoiceNumber}",
        });

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = DateOnly.FromDateTime(invoice.InvoiceDate.UtcDateTime),
            Source = JournalSource.Inventory,
            SourceType = "Invoice",
            SourceId = invoice.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"COGS / finished-goods relief — invoice {invoice.InvoiceNumber}",
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        var entry = await postingEngine.PostAsync(request, userId, ct);
        await TryAuditCogsAsync(invoice, entry, totalCogs, userId, ct);
    }

    /// <summary>A line relieves finished-goods inventory iff it carries a Part that is a (non-phantom)
    /// FinishedGood. Service / free-text lines (null Part) and non-FG parts do not.</summary>
    private static bool LineRelievesFinishedGoods(InvoiceLine line) =>
        line.Part is { } part
        && part.InventoryClass == InventoryClass.FinishedGood
        && part.ProcurementSource != ProcurementSource.Phantom;

    /// <summary>Resolved per-unit standard cost (documented read priority, Part.cs):
    /// ManualCostOverride ?? CurrentCostCalculation.ResultAmount ?? null.</summary>
    private static decimal? ResolveStandardCost(Part part) =>
        part.ManualCostOverride ?? part.CurrentCostCalculation?.ResultAmount;

    private async Task TryAuditCogsAsync(
        Invoice invoice, JournalEntry entry, decimal totalCogs, int actorUserId, CancellationToken ct)
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
                    totalCogs,
                },
                reason = $"Invoice {invoice.InvoiceNumber} — COGS / finished-goods relief posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlInvoiceCogsPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "COGS posting audit write failed for invoice {InvoiceId} (entry {EntryId}); posting itself is committed.",
                invoice.Id, entry.Id);
        }
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
