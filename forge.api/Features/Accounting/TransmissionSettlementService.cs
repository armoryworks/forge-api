using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — posts the bank-settlement entry (Dr CASH_IN_TRANSIT / Cr CASH) for a
/// transmission that the bank ACCEPTED. Extracted from PaymentTransmissionJob so the manual wire
/// attestation (banking.wire.manual-attestation) posts the identical entry through the identical
/// guards:
/// <list type="bullet">
///   <item>Skips silently when FULLGL is dark (no origination), CIT isn't seeded, or the
///         origination credited CASH directly (legacy) — exactly the job's prior behavior.</item>
///   <item>Idempotent via <c>AP:VendorPayment:{id}:SETTLEMENT</c>.</item>
///   <item>Posts under <see cref="GlSystemPostingScope"/>: the settlement is a mechanical
///         consequence of the accepted submission, not a discretionary journal — the same §5.7
///         system carve-out the Hangfire path uses (and HTTP attesters may lack GL posting roles).</item>
///   <item>NEVER throws to the caller — a settlement-posting failure must not unwind a Succeeded
///         transmission; the lingering CIT balance is the visible reconciling item.</item>
/// </list>
/// </summary>
public interface ITransmissionSettlementService
{
    Task TryPostSettlementAsync(PaymentTransmission transmission, string paymentNumber, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TransmissionSettlementService(
    AppDbContext db,
    IClock clock,
    IPostingEngine? postingEngine = null) : ITransmissionSettlementService
{
    public async Task TryPostSettlementAsync(
        PaymentTransmission transmission, string paymentNumber, CancellationToken ct = default)
    {
        if (postingEngine is null || transmission.SourceType != "VendorPayment")
            return;

        try
        {
            var book = await db.Books.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(ct);
            if (book is null)
                return; // No GL configured — nothing was originated, nothing to settle.

            var originationKey = $"{JournalSource.AP}:VendorPayment:{transmission.SourceId}:PAYMENT";
            var origination = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == originationKey, ct);
            if (origination is null)
                return; // FULLGL was off when the payment was created — skip silently.

            var citAccountId = await db.AccountDeterminationRules.AsNoTracking()
                .Where(r => r.BookId == book.Id && r.Key == "CASH_IN_TRANSIT")
                .Select(r => (int?)r.GlAccountId)
                .FirstOrDefaultAsync(ct);
            if (citAccountId is null)
                return; // CIT not seeded — pre-CIT behavior, nothing in transit.

            var inTransit = origination.Lines
                .Where(l => l.GlAccountId == citAccountId && l.Credit > 0m)
                .Sum(l => l.FunctionalAmount);
            if (inTransit <= 0m)
                return; // Origination credited CASH directly (legacy) — nothing to clear.

            var request = new PostingRequest
            {
                BookId = book.Id,
                EntryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                Source = JournalSource.AP,
                SourceType = "VendorPayment",
                SourceId = transmission.SourceId,
                CurrencyId = book.FunctionalCurrencyId,
                Memo = $"Bank settlement — payment {paymentNumber}",
                IdempotencyKey = $"{JournalSource.AP}:VendorPayment:{transmission.SourceId}:SETTLEMENT",
                Lines =
                [
                    new PostingLine
                    {
                        AccountKey = "CASH_IN_TRANSIT",
                        Debit = inTransit,
                        Description = $"Bank settlement — payment {paymentNumber}",
                    },
                    new PostingLine
                    {
                        AccountKey = "CASH",
                        Credit = inTransit,
                        Description = $"Bank settlement — payment {paymentNumber}",
                    },
                ],
            };

            // §5.7 system carve-out: trusted, idempotent, mechanical posting — authorized and
            // logged as the system principal (Hangfire has no principal; attesters may lack GL roles).
            using (GlSystemPostingScope.Enter())
            {
                await postingEngine.PostAsync(request, transmission.CreatedByUserId ?? 0, ct);
            }

            Log.Information(
                "Settlement posted for transmission {Id} ({Amount} in transit cleared).",
                transmission.Id, inTransit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Settlement posting failed for transmission {Id} (payment {Payment}); the transmission "
                + "remains Succeeded — the cash-in-transit balance will surface in reconciliation.",
                transmission.Id, paymentNumber);
        }
    }
}
