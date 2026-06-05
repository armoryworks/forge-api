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

    public async Task PostVendorBillApprovedAsync(
        int vendorBillId, int approvedByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default): zero behavior change while FULLGL is off ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        await PostCoreAsync(vendorBillId, approvedByUserId, ct);
    }

    private async Task PostCoreAsync(int vendorBillId, int approvedByUserId, CancellationToken ct)
    {
        var bill = await db.Set<VendorBill>()
            .Include(b => b.Lines)
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

        var lines = new List<PostingLine>(bill.Lines.Count + 2);

        // Dr each line's resolved account for the line total.
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
