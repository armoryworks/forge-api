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
/// Phase-2 STAGE A — Vendor-payment / cash-disbursement posting (ACCOUNTING_SUITE_PLAN §7 matrix
/// "VendorPayment"). The AP counterpart of <see cref="PaymentCashPostingService"/> (opposite direction):
/// when a <see cref="VendorPayment"/> is created and CAP-ACCT-FULLGL is on, posts inline:
/// <list type="bullet">
///   <item><b>Dr</b> <c>AP_CONTROL</c> for the <i>applied</i> amount — relieving the vendor payable
///         (party = vendor, §5.2).</item>
///   <item><b>Dr</b> <c>PREPAID_EXPENSE</c> for any <i>unapplied</i> amount — a vendor advance / prepayment
///         carried as an asset until applied to a bill (the asset-side mirror of customer deposits).</item>
///   <item><b>Cr</b> <c>CASH</c> for the full amount disbursed — or <b>Cr</b> <c>CASH_IN_TRANSIT</c> for
///         electronic methods (<see cref="PaymentMethods.IsElectronic"/>: BankTransfer/Wire), which only
///         record the <i>intent</i> to move money (architecture.md §7 BANK-002). The in-transit balance is
///         cleared (Dr CASH_IN_TRANSIT / Cr CASH) by the settlement entry the
///         <c>PaymentTransmissionJob</c> posts when the bank submission succeeds.</item>
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

    /// <summary>
    /// Reverses the posted cash-disbursement journal for a vendor payment being voided (equal-and-opposite
    /// entry — including any realized-FX plug lines — original flipped to Reversed). A no-op while
    /// CAP-ACCT-FULLGL is off, when nothing is posted for the payment (created while the capability was
    /// off), or when the origination is already reversed.
    /// </summary>
    Task ReverseVendorPaymentCreatedAsync(
        int vendorPaymentId, string reason, int reversedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class VendorPaymentCashPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null,
    // Optional / null-default so existing construction sites keep compiling; only the void-reversal
    // path needs a clock (the reversal posts on the void date). Falls back to system time when absent.
    IClock? clock = null) : IVendorPaymentCashPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    private const string KeyApControl = "AP_CONTROL";
    private const string KeyCash = "CASH";
    // §7 BANK-002 clearing: electronic disbursements credit CASH_IN_TRANSIT at origination (intent);
    // the transmission-success settlement entry moves it to CASH (confirmed).
    private const string KeyCashInTransit = "CASH_IN_TRANSIT";
    // Unapplied vendor cash = an advance/prepayment to the vendor (asset). Ratify per PHASE2_STATUS.
    private const string KeyVendorAdvance = "PREPAID_EXPENSE";
    // Realized FX (Phase-4): the difference between the AP carrying value (booking rate) and the cash paid
    // (settlement rate). FX_REVALUATION is for period-end UNrealized revaluation only — never here.
    private const string KeyFxGain = "FX_GAIN";
    private const string KeyFxLoss = "FX_LOSS";

    public async Task PostVendorPaymentCreatedAsync(
        int vendorPaymentId, int createdByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default) ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        await PostCoreAsync(vendorPaymentId, createdByUserId, ct);
    }

    public async Task ReverseVendorPaymentCreatedAsync(
        int vendorPaymentId, string reason, int reversedByUserId, CancellationToken ct = default)
    {
        // ── GATE (dark by default) ──
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);
        if (book is null)
            return; // No GL configured — nothing was ever posted for this payment.

        // The origination entry, located by its idempotency key. Skip if none is currently posted —
        // the payment may have been created while FULLGL was off, or the entry was already reversed.
        // (A settlement entry cannot exist here: void is blocked once the transmission Succeeded.)
        var idempotencyKey = $"{JournalSource.AP}:VendorPayment:{vendorPaymentId}:PAYMENT";
        var entry = await db.JournalEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == idempotencyKey, ct);

        if (entry is null || entry.Status != JournalEntryStatus.Posted || entry.ReversedByEntryId is not null)
            return;

        // Reverse on the VOID date (today) — the original period may differ; the engine guards closed
        // periods. ReverseAsync flips the WHOLE entry, including any realized-FX plug lines.
        var reversalDate = DateOnly.FromDateTime((clock?.UtcNow ?? DateTimeOffset.UtcNow).UtcDateTime);
        await postingEngine.ReverseAsync(
            entry.Id, reversalDate, $"Vendor payment {vendorPaymentId} voided: {reason}", reversedByUserId, ct);
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

        // ── Realized FX at settlement (Phase-4, ALL-FUNCTIONAL integrated entry — mirror of the AR side with
        // the FX sign flipped). Per application we relieve AP at the bill's BOOKING rate (its booked payable
        // carrying value) and pay cash out at the application's SETTLEMENT rate; the difference is realized FX.
        // The whole entry is in functional currency at FxRate 1, so it must balance on Dr/Cr exactly.
        //
        // BACKWARD COMPAT: when bill.FxRate == 1 AND app.SettlementFxRate == 1, apRelief == cash == applied and
        // the FX plug is 0 → byte-identical to the prior single-currency entry (no FX line).
        decimal cashFromApplications = 0m; // Σ cash_func (foreign × settlement rate)
        decimal apRelief = 0m;             // Σ apRelief_func (foreign × bill booking rate)

        foreach (var app in payment.Applications)
        {
            var foreign = app.Amount;

            // Load the bill for its booking FxRate. A not-found bill (defensive / isolated tests that
            // reference synthetic ids) defaults to rate 1 — the single-currency carrying value.
            var bookingRate = await db.Set<VendorBill>()
                .Where(b => b.Id == app.VendorBillId)
                .Select(b => (decimal?)b.FxRate)
                .FirstOrDefaultAsync(ct) ?? 1m;

            apRelief += Math.Round(foreign * bookingRate, 2, MidpointRounding.AwayFromZero);
            cashFromApplications += Math.Round(foreign * app.SettlementFxRate, 2, MidpointRounding.AwayFromZero);
        }

        // unapplied = the FUNCTIONAL cash NOT consumed by applications = advance / on-account (asset), carried
        // at the payment's rate. payment.Amount (functional cash) − Σ cash_func so foreign-currency applications
        // net out cleanly. SINGLE CURRENCY (rate 1): cashFromApplications == AppliedAmount, so this equals
        // payment.UnappliedAmount — byte-identical to the prior behavior. (A tiny negative from rounding floors
        // to 0 so a 0/0 line is never emitted.)
        var unapplied = Math.Max(0m, payment.Amount - cashFromApplications);

        var lines = new List<PostingLine>(4);

        // Dr AP at the booked carrying value (booking rate), fully relieving the payable (party = vendor).
        if (apRelief > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = payment.VendorId,
                Debit = apRelief,
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

        // Cr Cash = Σ cash_func (+ unapplied advance at the payment's rate). Electronic methods credit the
        // CASH_IN_TRANSIT clearing account instead of CASH — origination records the INTENT to move money;
        // the confirmed settlement (Dr CIT / Cr CASH) posts when the bank transmission succeeds (§7 BANK-002).
        var cashCredit = cashFromApplications + unapplied;
        if (cashCredit > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = PaymentMethods.IsElectronic(payment.Method) ? KeyCashInTransit : KeyCash,
                Credit = cashCredit,
                Description = $"Cash disbursement — vendor payment {payment.PaymentNumber}",
            });
        }

        // FX plug = Σ (apRelief − cash). We relieved AP at the booking rate but cash went out at settlement;
        //   > 0  → settled a BIGGER payable for LESS cash (the currency weakened) → Cr FX_GAIN;
        //   < 0  → paid MORE cash than the payable relieved (the currency strengthened) → Dr FX_LOSS.
        // Worked example: EUR bill foreign 100, booking 1.10 → AP 110; settle 1.05 → cash 105 →
        //   plug +5 → Dr AP 110 / Cr Cash 105 / Cr FX_GAIN 5 (balances; that bill's AP nets to 0).
        var fxPlug = apRelief - cashFromApplications;
        if (fxPlug > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyFxGain,
                Credit = fxPlug,
                Description = $"Realized FX gain — vendor payment {payment.PaymentNumber}",
            });
        }
        else if (fxPlug < 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyFxLoss,
                Debit = -fxPlug,
                Description = $"Realized FX loss — vendor payment {payment.PaymentNumber}",
            });
        }

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

        await TryAuditAsync(payment, entry, amount, payment.AppliedAmount, unapplied, createdByUserId, ct);
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
