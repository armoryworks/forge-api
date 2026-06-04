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
/// Phase-1 STAGE B — Payment / cash-receipt posting (ACCOUNTING_SUITE_PLAN §7
/// matrix row "Payment applied", §8). When a customer payment is created, this
/// service posts the cash-receipt journal entry <b>inline, in the operational
/// command's transaction</b> (the locked inline model — §2, §7): it adds the
/// entry to the shared request-scoped <see cref="AppDbContext"/> and the engine's
/// <c>SaveChangesAsync</c> joins the caller's unit of work.
///
/// <para><b>Posting (per §7 / matrix row "Payment applied"):</b>
/// <list type="bullet">
///   <item><b>Dr CASH</b> for the full payment amount received.</item>
///   <item><b>Cr AR_CONTROL</b> for the <i>applied</i> amount — relieving the
///         customer's receivable (party = the customer on this control line, §5.2).</item>
///   <item><b>Cr CUSTOMER_DEPOSITS</b> for any <i>unapplied</i> (overpayment /
///         on-account) amount — a liability until it's applied to an invoice.</item>
/// </list>
/// The entry balances because <c>Amount == applied + unapplied</c> (see
/// <see cref="Payment.AppliedAmount"/> / <see cref="Payment.UnappliedAmount"/>).
/// </para>
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default).</b> The first
/// thing <see cref="PostPaymentCreatedAsync"/> does is gate on the capability
/// snapshot; with FULLGL off it returns immediately as a no-op — zero behavior
/// change to the operational payment flow. Only when FULLGL is enabled does it
/// resolve the book and post. Once FULLGL is ON a posting failure propagates and
/// fails the operation (the inline model's "fail visibly" rule — §2); the dark
/// (OFF) path can never be perturbed because it returns before touching the
/// engine or the <c>acct_*</c> tables.</para>
/// </summary>
public interface IPaymentCashPostingService
{
    /// <summary>
    /// Posts the cash-receipt journal for a newly created payment, when (and only
    /// when) CAP-ACCT-FULLGL is enabled. A no-op while the capability is off.
    /// Idempotent: a re-post of the same payment returns the existing entry via
    /// the engine's <c>(BookId, IdempotencyKey)</c> de-dupe.
    /// </summary>
    /// <param name="paymentId">The payment that was created (must already be persisted with its Id).</param>
    /// <param name="createdByUserId">Server-trusted actor (recorded as PostedBy + audit actor).</param>
    Task PostPaymentCreatedAsync(int paymentId, int createdByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PaymentCashPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IPaymentCashPostingService
{
    // The capability that gates the whole GL. Must match CapabilityCatalog.
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    // Determination keys (must match SeedData.Accounting.cs). Business events
    // never hardcode account numbers — they resolve a (BookId, Key) rule (§5.1).
    private const string KeyCash = "CASH";
    private const string KeyArControl = "AR_CONTROL";
    private const string KeyCustomerDeposits = "CUSTOMER_DEPOSITS";

    public async Task PostPaymentCreatedAsync(
        int paymentId, int createdByUserId, CancellationToken ct = default)
    {
        // ── GATE 1 (dark by default): zero behavior change while FULLGL is off ──
        // This is the single most important guard. With CAP-ACCT-FULLGL OFF
        // (the default) the method returns before touching the engine or the
        // acct_* tables, so the operational payment flow is byte-for-byte
        // unchanged and the existing payment tests pass unmodified.
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        // From here on FULLGL is ON. A posting failure SHOULD fail the operation
        // visibly (inline model, §2) — so once past the dark gate we let
        // PostingException propagate.
        await PostCoreAsync(paymentId, createdByUserId, ct);
    }

    private async Task PostCoreAsync(int paymentId, int createdByUserId, CancellationToken ct)
    {
        // Load the payment with its applications from the SHARED request-scoped
        // context (so the posting joins the caller's transaction).
        var payment = await db.Set<Payment>()
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null)
        {
            // The operational handler already created it; if it's gone here
            // something is wrong, but don't invent a ledger entry.
            Log.Warning(
                "Payment posting skipped: payment {PaymentId} not found when posting (FULLGL on).",
                paymentId);
            return;
        }

        // ── Resolve the single seeded posting book (single-entity for now, §5.1).
        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (book is null)
        {
            // FULLGL is on but no book is seeded — a real misconfiguration.
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the payment into.");
        }

        // EntryDate = the payment date in the book's reporting context. DateOnly
        // is immune to UTC normalization (§5.1).
        var entryDate = DateOnly.FromDateTime(payment.PaymentDate.UtcDateTime);

        var amount = payment.Amount;
        if (amount <= 0m)
        {
            // A zero/negative payment has nothing to post; skip rather than emit a
            // degenerate entry the balanced-check would reject anyway. (The
            // operational validator already requires Amount > 0.)
            Log.Information(
                "Payment posting skipped: payment {PaymentId} has non-positive amount {Amount}.",
                paymentId, amount);
            return;
        }

        // applied = Σ application amounts; unapplied = overpayment / on-account.
        // Amount == applied + unapplied (Payment.UnappliedAmount), so the entry
        // below is balanced by construction.
        var applied = payment.AppliedAmount;
        var unapplied = payment.UnappliedAmount;

        var lines = new List<PostingLine>(3)
        {
            // Dr CASH for the full amount received.
            new()
            {
                AccountKey = KeyCash,
                Debit = amount,
                Description = $"Cash receipt — payment {payment.PaymentNumber}",
            },
        };

        // Cr AR for the applied portion, relieving the customer's receivable. AR
        // is a control account → the engine requires the customer party here (§5.2).
        if (applied > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = payment.CustomerId,
                Credit = applied,
                Description = $"AR settlement — payment {payment.PaymentNumber}",
            });
        }

        // Cr CUSTOMER_DEPOSITS for any unapplied (overpayment / on-account) cash —
        // a liability carried until it's later applied to an invoice.
        if (unapplied > 0m)
        {
            lines.Add(new PostingLine
            {
                AccountKey = KeyCustomerDeposits,
                Credit = unapplied,
                Description = $"Customer deposit (unapplied) — payment {payment.PaymentNumber}",
            });
        }

        // ── Idempotency key (§5.2): source:type:id:purpose. AR/PAYMENT for a
        // payment; a re-post returns the existing entry (no throw, no dup).
        var idempotencyKey = $"{JournalSource.AR}:Payment:{payment.Id}:PAYMENT";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AR,
            SourceType = "Payment",
            SourceId = payment.Id,
            CurrencyId = book.FunctionalCurrencyId, // Phase-0/1 single-currency invariant
            Memo = $"Cash receipt — payment {payment.PaymentNumber}"
                 + (unapplied > 0m ? $" (unapplied {unapplied} to customer deposits)" : string.Empty),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        // ── Post inline on the shared context. The engine validates, assigns the
        // EntryNumber, maintains LedgerBalance, and calls SaveChangesAsync so the
        // write participates in the operational command's transaction (§2, §5.2).
        var entry = await postingEngine.PostAsync(request, createdByUserId, ct);

        // ── Audit (§5.8): actor + before/after + reason on post. Best-effort —
        // an audit hiccup must not unwind a successful, committed posting.
        await TryAuditAsync(payment, entry, amount, applied, unapplied, createdByUserId, ct);
    }

    private async Task TryAuditAsync(
        Payment payment,
        JournalEntry entry,
        decimal amount,
        decimal applied,
        decimal unapplied,
        int actorUserId,
        CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null, // a payment create posts the cash receipt; no prior ledger state
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    source = entry.Source.ToString(),
                    entryDate = entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount,
                    applied,
                    unapplied,
                },
                reason = $"Payment {payment.PaymentNumber} created — cash receipt posted.",
            });

            await auditWriter.WriteAsync(
                action: "GlPaymentCashPosted",
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
                "Payment posting audit write failed for payment {PaymentId} (entry {EntryId}); posting itself is committed.",
                payment.Id, entry.Id);
        }
    }
}
