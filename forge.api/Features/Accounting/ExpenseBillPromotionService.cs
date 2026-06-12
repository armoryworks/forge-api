using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — promotes a vendor-settled <see cref="Expense"/> into the AP bill pipeline
/// on approval, so the payable flows through the ONE set of AP machinery: the bill posting books
/// Dr Expense / Cr AP (party = vendor) under the bill's idempotency key, the <c>ApOpenItem</c> is
/// created in the same transaction, the bill ages, and it is settled by ordinary vendor payments
/// (including electronic transmission / cash-in-transit clearing). Without promotion a vendor-settled
/// expense credits AP control with no open item and NO settlement path — a standing reconciliation
/// difference nothing in the app can relieve.
///
/// <para><b>Gating.</b> Promotion requires CAP-P2P-BILL (the Payables feature). When it is off — or the
/// expense is cash-settled, vendor-less, or degenerate — <see cref="PromoteApprovedExpenseAsync"/>
/// returns null and the caller falls back to the legacy <see cref="IExpenseApPostingService"/> posting
/// (itself dark until CAP-ACCT-FULLGL). GL posting of the promoted bill self-gates on FULLGL inside
/// <see cref="IVendorBillApPostingService"/>, so with Payables on and the GL dark the bill is still
/// created (operationally payable) and simply never posts — the §7A opening-balance cutover owns
/// dark-period history.</para>
///
/// <para><b>Idempotency.</b> At most one live (non-void) bill per expense
/// (<c>ux_vendor_bills_expense_live</c> is the DB backstop); a re-approve returns the existing bill.
/// An expense whose payable already entered the GL under the LEGACY <c>AP:Expense:{id}:EXPENSE</c>
/// origination (pre-promotion upgrade, reconstructed by the boot backfill) is never re-posted under
/// the bill key — that would double-book the AP credit.</para>
///
/// <para><b>Demotion.</b> When an approved expense leaves approved status (rejected / revision /
/// re-opened), <see cref="DemoteExpenseBillAsync"/> voids the promoted bill and reverses its posting
/// (legacy-keyed or bill-keyed). A bill with vendor payments applied blocks the transition — void the
/// payment(s) first, exactly like <c>VoidVendorBill</c>.</para>
/// </summary>
public interface IExpenseBillPromotionService
{
    /// <summary>
    /// Creates (or returns) the promoted vendor bill for an approved vendor-settled expense and posts
    /// it when the GL is on. Returns null when promotion does not apply (Payables capability off,
    /// cash-settled, no vendor, or non-positive amount) — the caller then falls back to the legacy
    /// expense AP posting. Runs on the shared request-scoped context inside the caller's transaction.
    /// </summary>
    Task<VendorBill?> PromoteApprovedExpenseAsync(int expenseId, int approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Voids the live promoted bill (if any) for an expense leaving approved status, reversing its
    /// posting. Throws when the bill has vendor payments applied (void the payments first). A no-op
    /// when no live promoted bill exists. Not capability-gated — a bill created while Payables was on
    /// must remain demotable even if the capability is later disabled.
    /// </summary>
    Task DemoteExpenseBillAsync(int expenseId, int actorUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ExpenseBillPromotionService(
    AppDbContext db,
    IVendorBillRepository billRepo,
    IVendorBillApPostingService billPosting,
    ICapabilitySnapshotProvider capabilities) : IExpenseBillPromotionService
{
    // The Payables (vendor bill) feature gate. Must match CapabilityCatalog.
    private const string BillCapability = "CAP-P2P-BILL";

    public async Task<VendorBill?> PromoteApprovedExpenseAsync(
        int expenseId, int approvedByUserId, CancellationToken ct = default)
    {
        // ── GATE: Payables off → decline; the caller falls back to the legacy expense AP posting,
        // preserving pre-promotion behavior exactly (including its FULLGL dark gate).
        if (!capabilities.IsEnabled(BillCapability))
            return null;

        var expense = await db.Expenses
            .Include(e => e.Vendor)
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct);
        if (expense is null)
            return null;

        // Settlement disambiguation — the same rule as ExpenseApPostingService: explicit target wins,
        // else a present vendor implies AP. Cash-settled expenses keep the legacy Dr Expense / Cr Cash
        // posting (they never touch AP control, so no bill / open item is needed).
        var settlesToAp = expense.SettlementTarget switch
        {
            ExpenseSettlementTarget.AccountsPayable => true,
            ExpenseSettlementTarget.Cash => false,
            _ => expense.VendorId is not null,
        };

        // No vendor → can't carry the AP party → decline; the legacy service surfaces the
        // EXPENSE_AP_NO_VENDOR misconfiguration when FULLGL is on (no-op while dark). Non-positive
        // amounts are degenerate (the bill validator and the engine both reject zero totals).
        if (!settlesToAp || expense.VendorId is null || expense.Amount <= 0m)
            return null;

        // Was this payable already booked under the LEGACY Expense-keyed origination? (Upgrade path:
        // approved pre-promotion with FULLGL on; the boot backfill reconstructs the bill + open item.)
        // If so the AP credit is already in the GL — posting the bill key too would double-book.
        var legacyPosted = await HasPostedLegacyOriginationAsync(expense.Id, ct);

        // ── Idempotency: one live bill per expense; a re-approve returns it.
        var existing = await db.VendorBills
            .FirstOrDefaultAsync(b => b.ExpenseId == expense.Id && b.Status != VendorBillStatus.Void, ct);
        if (existing is not null)
        {
            if (!legacyPosted)
                await billPosting.PostVendorBillApprovedAsync(existing.Id, approvedByUserId, ct);
            return existing;
        }

        var (terms, termsDays) = MapVendorTerms(expense.Vendor?.PaymentTerms);

        var bill = new VendorBill
        {
            BillNumber = await billRepo.GenerateNextBillNumberAsync(ct),
            VendorId = expense.VendorId.Value,
            ExpenseId = expense.Id,
            CurrencyId = await ResolveFunctionalCurrencyIdAsync(ct),
            FxRate = 1m, // expenses are functional-currency documents
            // Born APPROVED: the expense approval IS the payable's approval — routing it through a
            // second Draft→Approved gate would double-approve one business event. Posting fires below
            // exactly as ApproveVendorBill would.
            Status = VendorBillStatus.Approved,
            BillDate = expense.ExpenseDate,
            DueDate = expense.ExpenseDate.AddDays(termsDays),
            CreditTerms = terms,
            Notes = $"Promoted from expense EXP-{expense.Id} on approval.",
            Lines =
            {
                new VendorBillLine
                {
                    Description = $"Expense EXP-{expense.Id} — {expense.Category}: {expense.Description}",
                    Quantity = 1m,
                    UnitPrice = expense.Amount,
                    LineNumber = 1,
                    AccountDeterminationKey = "OPERATING_EXPENSE",
                    JobId = expense.JobId, // carry the job tag to the GL debit (job costing)
                },
            },
        };

        db.VendorBills.Add(bill);
        await db.SaveChangesAsync(ct); // assign the id so the posting + activity can reference it

        db.LogActivityAt(
            "created",
            $"Bill {bill.BillNumber} promoted from expense EXP-{expense.Id} and approved for payment — {bill.Total:C}",
            ("VendorBill", bill.Id));

        if (legacyPosted)
        {
            Log.Information(
                "Expense {ExpenseId} promoted to bill {BillNumber} without posting — its AP credit is already "
                + "booked under the legacy Expense-keyed origination.",
                expense.Id, bill.BillNumber);
        }
        else
        {
            // Self-gates on CAP-ACCT-FULLGL; while dark the bill exists operationally and never posts.
            await billPosting.PostVendorBillApprovedAsync(bill.Id, approvedByUserId, ct);
        }

        await db.SaveChangesAsync(ct);
        return bill;
    }

    public async Task DemoteExpenseBillAsync(int expenseId, int actorUserId, CancellationToken ct = default)
    {
        var bill = await db.VendorBills
            .Include(b => b.PaymentApplications)
            .FirstOrDefaultAsync(b => b.ExpenseId == expenseId && b.Status != VendorBillStatus.Void, ct);
        if (bill is null)
            return;

        // Mirrors VoidVendorBill: a paid/partially-paid bill can't be unwound under the payment —
        // the payment's AP debit would point at a reversed liability. Block the expense transition.
        if (bill.PaymentApplications.Any())
            throw new InvalidOperationException(
                $"Expense EXP-{expenseId} cannot leave approved status: its promoted bill {bill.BillNumber} "
                + "has vendor payment(s) applied. Void the payment(s) first.");

        bill.Status = VendorBillStatus.Void;

        // Reverses the bill-keyed posting, or the legacy Expense-keyed origination for backfilled
        // bills (see ReverseVendorBillApprovedAsync). Self-gates on FULLGL; flips the open item to
        // Voided in the same transaction.
        await billPosting.ReverseVendorBillApprovedAsync(bill.Id, actorUserId, ct);

        db.LogActivityAt(
            "voided",
            $"Bill {bill.BillNumber} voided — expense EXP-{expenseId} left approved status",
            ("VendorBill", bill.Id));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// True when the expense's payable is already in the GL under the legacy
    /// <c>AP:Expense:{id}:EXPENSE</c> origination — Posted, and with a vendor-party line (the shared
    /// idempotency key also covers the cash-settled variant, which carries no party and no payable).
    /// </summary>
    private Task<bool> HasPostedLegacyOriginationAsync(int expenseId, CancellationToken ct)
        => db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.Source == JournalSource.AP
                && e.SourceType == "Expense"
                && e.SourceId == expenseId
                && e.Status == JournalEntryStatus.Posted
                && e.Lines.Any(l => l.SubledgerPartyType == SubledgerPartyType.Vendor), ct);

    /// <summary>
    /// Maps the vendor's freeform <c>PaymentTerms</c> string onto <see cref="CreditTerms"/> ("Net 30",
    /// "net30", "NET-30" → Net30, etc.). Unrecognized / absent terms default to DueOnReceipt — the
    /// conservative choice (the bill surfaces as due immediately rather than silently far-dated).
    /// </summary>
    private static (CreditTerms Terms, int Days) MapVendorTerms(string? vendorPaymentTerms)
    {
        var normalized = new string(
            (vendorPaymentTerms ?? string.Empty).Where(char.IsLetterOrDigit).ToArray())
            .ToLowerInvariant();

        return normalized switch
        {
            "net15" => (CreditTerms.Net15, 15),
            "net30" => (CreditTerms.Net30, 30),
            "net45" => (CreditTerms.Net45, 45),
            "net60" => (CreditTerms.Net60, 60),
            "net90" => (CreditTerms.Net90, 90),
            _ => (CreditTerms.DueOnReceipt, 0),
        };
    }

    /// <summary>The active book's functional currency, or the seeded default (1) when no book exists.</summary>
    private async Task<int> ResolveFunctionalCurrencyIdAsync(CancellationToken ct)
        => await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct) is int id and > 0 ? id : 1;
}
