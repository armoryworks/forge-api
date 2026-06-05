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
/// Phase-2 STAGE A — Vendor-payment / cash-disbursement posting (ACCOUNTING_SUITE_PLAN §7 matrix
/// "VendorPayment"). The AP counterpart of <see cref="PaymentCashPostingService"/> (opposite direction):
/// when a <see cref="VendorPayment"/> is created and CAP-ACCT-FULLGL is on, posts inline:
/// <list type="bullet">
///   <item><b>Dr</b> <c>AP_CONTROL</c> for the <i>applied</i> amount — relieving the vendor payable
///         (party = vendor, §5.2).</item>
///   <item><b>Dr</b> <c>PREPAID_EXPENSE</c> for any <i>unapplied</i> amount — a vendor advance / prepayment
///         carried as an asset until applied to a bill (the asset-side mirror of customer deposits).</item>
///   <item><b>Cr</b> <c>CASH</c> for the full amount disbursed.</item>
/// </list>
/// The entry balances because <c>Amount == applied + unapplied</c>.
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default).</b> Idempotent via the engine's
/// (BookId, IdempotencyKey) de-dupe.</para>
/// </summary>
public interface IVendorPaymentCashPostingService
{
    /// <summary>
    /// Posts the cash-disbursement journal for a newly created vendor payment, when (and only when)
    /// CAP-ACCT-FULLGL is enabled. A no-op while the capability is off. Idempotent.
    /// </summary>
    Task PostVendorPaymentCreatedAsync(int vendorPaymentId, int createdByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VendorPaymentCashPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IVendorPaymentCashPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyApControl = "AP_CONTROL";
    private const string KeyCash = "CASH";
    // Unapplied vendor cash = an advance/prepayment to the vendor (asset). Ratify per PHASE2_STATUS.
    private const string KeyVendorAdvance = "PREPAID_EXPENSE";

    public async Task PostVendorPaymentCreatedAsync(
        int vendorPaymentId, int createdByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default) ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        await PostCoreAsync(vendorPaymentId, createdByUserId, ct);
    }

    private async Task PostCoreAsync(int vendorPaymentId, int createdByUserId, CancellationToken ct)
    {
        var payment = await db.Set<VendorPayment>()
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Id == vendorPaymentId, ct);

        if (payment is null)
        {
            Log.Warning(
                "Vendor-payment posting skipped: payment {VendorPaymentId} not found when posting (FULLGL on).",
                vendorPaymentId);
            return;
        }

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the vendor payment into.");

        var amount = payment.Amount;
        if (amount <= 0m)
        {
            Log.Information(
                "Vendor-payment posting skipped: payment {VendorPaymentId} has non-positive amount {Amount}.",
                vendorPaymentId, amount);
            return;
        }

        var entryDate = DateOnly.FromDateTime(payment.PaymentDate.UtcDateTime);

        var applied = payment.AppliedAmount;
        var unapplied = payment.UnappliedAmount;

        var lines = new List<PostingLine>(3);

        // Dr AP for the applied portion, relieving the vendor's payable (party = vendor).
        if (applied > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = payment.VendorId,
                Debit = applied,
                Description = $"AP settlement — vendor payment {payment.PaymentNumber}",
            });
        }

        // Dr vendor advance (asset) for any unapplied (prepayment / on-account) cash.
        if (unapplied > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyVendorAdvance,
                Debit = unapplied,
                Description = $"Vendor advance (unapplied) — vendor payment {payment.PaymentNumber}",
            });
        }

        // Cr Cash for the full amount disbursed.
        lines.Add(new PostingLine
        {
            AccountKey = KeyCash,
            Credit = amount,
            Description = $"Cash disbursement — vendor payment {payment.PaymentNumber}",
        });

        var idempotencyKey = $"{JournalSource.AP}:VendorPayment:{payment.Id}:PAYMENT";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "VendorPayment",
            SourceId = payment.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Cash disbursement — vendor payment {payment.PaymentNumber}"
                 + (unapplied > 0m ? $" (advance {unapplied})" : string.Empty),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        var entry = await postingEngine.PostAsync(request, createdByUserId, ct);

        await TryAuditAsync(payment, entry, amount, applied, unapplied, createdByUserId, ct);
    }

    private async Task TryAuditAsync(
        VendorPayment payment, JournalEntry entry, decimal amount, decimal applied, decimal unapplied,
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
                    vendorId = payment.VendorId,
                    amount,
                    applied,
                    unapplied,
                },
                reason = $"Vendor payment {payment.PaymentNumber} created — cash disbursement posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlVendorPaymentCashPosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Vendor-payment posting audit write failed for payment {VendorPaymentId} (entry {EntryId}); posting is committed.",
                payment.Id, entry.Id);
        }
    }
}
