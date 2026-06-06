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
/// Phase-2 STAGE C — PO-receipt inventory / GRNI posting (ACCOUNTING_SUITE_PLAN §7 matrix
/// "PO receipt", §8.1). When goods are received against a purchase order, this service posts the
/// landed-cost journal <b>inline, in the receiving command's transaction</b> for the receipt
/// (all <see cref="ReceivingRecord"/>s sharing a <c>ReceiptNumber</c>):
/// <list type="bullet">
///   <item><b>Dr INVENTORY_{RAW|WIP|FG}</b> (per the part's <see cref="InventoryClass"/>) — or
///         <b>Dr OPERATING_EXPENSE</b> for consumables/tools — at <c>PO unit price × qty + allocated
///         freight</c> (landed).</item>
///   <item><b>Cr GRNI</b> (Goods Received Not Invoiced) for the base (PO price × qty) — the accrued
///         vendor liability until the bill arrives and the 3-way match clears it (STAGE D).</item>
///   <item><b>Cr FREIGHT_CLEARING</b> for the allocated freight — cleared when the freight bill lands.</item>
/// </list>
/// The entry balances by construction: Σ Dr (base + freight) == Σ GRNI (base) + Σ Freight (freight).
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default).</b> Idempotent via the engine's
/// (BookId, IdempotencyKey) de-dupe, keyed on the receipt number. <b>Costing:</b> inventory is valued at
/// the actual PO purchase price (landed); the standard-cost variance (PPV) is recognized at the 3-way bill
/// match (STAGE D), not here. GRNI / FREIGHT_CLEARING are non-control (party-less); INVENTORY_* are
/// inventory control accounts that post party-less (reconciled by part via the valuation store, §8.1).</para>
/// </summary>
public interface IReceiptInventoryPostingService
{
    /// <summary>
    /// Posts the inventory / GRNI journal for a PO receipt (the <see cref="ReceivingRecord"/>s sharing
    /// <paramref name="receiptNumber"/>), when (and only when) CAP-ACCT-FULLGL is enabled. A no-op while
    /// the capability is off, or when the receipt has no number (the single-line inventory receive path).
    /// </summary>
    Task PostReceiptAsync(
        int purchaseOrderId, string? receiptNumber, DateOnly entryDate, int receivedByUserId,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ReceiptInventoryPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IReceiptInventoryPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyInventoryRaw = "INVENTORY_RAW";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyInventoryFg = "INVENTORY_FG";
    private const string KeyGrni = "GRNI";
    private const string KeyFreightClearing = "FREIGHT_CLEARING";
    // Consumables / tools are not stocked-for-production inventory (per the InventoryClass doc comments:
    // issued to overhead / durable, not a BOM input) — expensed at receipt. A future INVENTORY_SUPPLIES
    // key could perpetual-stock consumables; high-value tools could capitalize as an Asset (no
    // capitalization-threshold signal exists at this layer yet). Decision recorded in PHASE2_STATUS.
    private const string KeyOperatingExpense = "OPERATING_EXPENSE";

    public async Task PostReceiptAsync(
        int purchaseOrderId, string? receiptNumber, DateOnly entryDate, int receivedByUserId,
        CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        // The single-line inventory receive path (Features/Inventory/ReceivePurchaseOrder) leaves
        // ReceiptNumber null and isn't posted here — STAGE C hooks the primary, freight-bearing
        // ReceiveItems flow. (Hooking that secondary path is a tracked follow-up.)
        if (string.IsNullOrWhiteSpace(receiptNumber))
            return;

        await PostCoreAsync(purchaseOrderId, receiptNumber, entryDate, receivedByUserId, ct);
    }

    private async Task PostCoreAsync(
        int purchaseOrderId, string receiptNumber, DateOnly entryDate, int userId, CancellationToken ct)
    {
        var records = await db.Set<ReceivingRecord>()
            .Include(r => r.PurchaseOrderLine).ThenInclude(l => l.Part)
            // Scope by PO id too (a single receive is for one PO) so a ReceiptNumber collision across POs
            // can never pull a foreign PO's lines into this journal.
            .Where(r => r.ReceiptNumber == receiptNumber && r.PurchaseOrderLine.PurchaseOrderId == purchaseOrderId)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            Log.Warning(
                "Receipt posting skipped: no receiving records found for receipt {ReceiptNumber} (FULLGL on).",
                receiptNumber);
            return;
        }

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the receipt into.");

        var lines = new List<PostingLine>(records.Count + 2);
        decimal totalBase = 0m;
        decimal totalFreight = 0m;

        foreach (var rec in records.OrderBy(r => r.Id))
        {
            var line = rec.PurchaseOrderLine;
            var baseCost = rec.QuantityReceived * line.UnitPrice;
            var freight = rec.AllocatedFreight ?? 0m;
            if (baseCost + freight <= 0m)
                continue; // nothing to capitalize for this line

            lines.Add(new PostingLine
            {
                AccountKey = DebitKeyFor(line.Part),
                Debit = baseCost + freight,
                Description = $"Receipt {receiptNumber} — {(line.Part?.PartNumber ?? $"part {line.PartId}")} x{rec.QuantityReceived}",
            });
            totalBase += baseCost;
            totalFreight += freight;
        }

        if (totalBase + totalFreight <= 0m)
            return; // a zero-value receipt has nothing to post

        // Cr GRNI for the base — the accrued vendor liability until the bill arrives (non-control, party-less).
        if (totalBase > 0m)
            lines.Add(new PostingLine
            {
                AccountKey = KeyGrni,
                Credit = totalBase,
                Description = $"GRNI — receipt {receiptNumber}",
            });

        // Cr Freight-Clearing for the landed freight — cleared when the freight bill lands.
        if (totalFreight > 0m)
            lines.Add(new PostingLine
            {
                AccountKey = KeyFreightClearing,
                Credit = totalFreight,
                Description = $"Freight clearing — receipt {receiptNumber}",
            });

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "Receipt",
            SourceId = purchaseOrderId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"PO receipt {receiptNumber} — inventory / GRNI (PO {purchaseOrderId})",
            IdempotencyKey = $"{JournalSource.Inventory}:Receipt:{purchaseOrderId}:{receiptNumber}:RECEIPT",
            Lines = lines,
        };

        var entry = await postingEngine.PostAsync(request, userId, ct);
        await TryAuditAsync(receiptNumber, purchaseOrderId, entry, totalBase, totalFreight, userId, ct);
    }

    /// <summary>Maps a received part's <see cref="InventoryClass"/> to the inventory determination key
    /// it capitalizes to. Consumables / tools are expensed (not stocked-for-production); a null/unknown
    /// class defaults to raw-materials inventory (a purchased input).</summary>
    private static string DebitKeyFor(Part? part) => part?.InventoryClass switch
    {
        InventoryClass.Raw or InventoryClass.Component => KeyInventoryRaw,
        InventoryClass.Subassembly => KeyInventoryWip,
        InventoryClass.FinishedGood => KeyInventoryFg,
        InventoryClass.Consumable or InventoryClass.Tool => KeyOperatingExpense,
        _ => KeyInventoryRaw,
    };

    private async Task TryAuditAsync(
        string receiptNumber, int purchaseOrderId, JournalEntry entry, decimal totalBase, decimal totalFreight,
        int actorUserId, CancellationToken ct)
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
                    purchaseOrderId,
                    receiptNumber,
                    totalBase,
                    totalFreight,
                },
                reason = $"PO receipt {receiptNumber} — inventory / GRNI posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlReceiptInventoryPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Receipt posting audit write failed for receipt {ReceiptNumber} (entry {EntryId}); posting itself is committed.",
                receiptNumber, entry.Id);
        }
    }
}
