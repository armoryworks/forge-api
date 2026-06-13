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
/// Phase-1 STAGE C — Expense / AP posting (ACCOUNTING_SUITE_PLAN §7 matrix row
/// "Expense approved", §6 Phase-1 row). When an expense transitions to an
/// approved state, this service posts the expense journal entry <b>inline, in the
/// operational command's transaction</b> (the locked inline model — §2, §7): it
/// adds the entry to the shared request-scoped <see cref="AppDbContext"/> and the
/// engine's <c>SaveChangesAsync</c> joins the caller's unit of work.
///
/// <para><b>Posting (per §7 / matrix "Expense approved"):</b>
/// <list type="bullet">
///   <item><b>Dr OPERATING_EXPENSE</b> for the expense amount.</item>
///   <item><b>Cr AP_CONTROL</b> (party = the vendor) when the expense settles to
///         a vendor — i.e. <see cref="Expense.SettlementTarget"/> is
///         <see cref="ExpenseSettlementTarget.AccountsPayable"/>, or it is unset
///         but a <see cref="Expense.VendorId"/> is present.</item>
///   <item><b>Cr CASH</b> otherwise (out-of-pocket / card / petty cash — no party).</item>
/// </list>
/// AP is a control account, so the engine requires the vendor party on the AP
/// credit line (§5.2): a vendor-settled expense with no vendor is a hard error
/// (surfaced once FULLGL is on, never while dark).
/// </para>
///
/// <para><b>STAYS DARK while CAP-ACCT-FULLGL is OFF (the default).</b> The first
/// thing <see cref="PostExpenseApprovedAsync"/> does is gate on the capability
/// snapshot; with FULLGL off it returns immediately as a no-op — zero behavior
/// change to the operational expense-approval flow, so the existing expense tests
/// pass unmodified. Only when FULLGL is enabled does it resolve the book and
/// post. Once FULLGL is ON a posting failure propagates and fails the operation
/// (the inline model's "fail visibly" rule — §2); the dark (OFF) path can never
/// be perturbed because it returns before touching the engine or the
/// <c>acct_*</c> tables.</para>
/// </summary>
public interface IExpenseApPostingService
{
    /// <summary>
    /// Posts the expense / AP (or cash) journal for an approved expense, when
    /// (and only when) CAP-ACCT-FULLGL is enabled. A no-op while the capability
    /// is off. Idempotent: a re-approve of the same expense returns the existing
    /// entry via the engine's <c>(BookId, IdempotencyKey)</c> de-dupe.
    /// </summary>
    /// <param name="expenseId">The expense being approved.</param>
    /// <param name="approvedByUserId">Server-trusted actor (recorded as PostedBy + audit actor).</param>
    Task PostExpenseApprovedAsync(int expenseId, int approvedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ExpenseApPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IExpenseApPostingService
{
    // The capability that gates the whole GL. Must match CapabilityCatalog.
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    // Determination keys (must match SeedData.Accounting.cs). Business events
    // never hardcode account numbers — they resolve a (BookId, Key) rule (§5.1).
    private const string KeyOperatingExpense = "OPERATING_EXPENSE";
    private const string KeyApControl = "AP_CONTROL";
    private const string KeyCash = "CASH";

    public async Task PostExpenseApprovedAsync(
        int expenseId, int approvedByUserId, CancellationToken ct = default)
    {
        // ── GATE 1 (dark by default): zero behavior change while FULLGL is off ──
        // This is the single most important guard. With CAP-ACCT-FULLGL OFF
        // (the default) the method returns before touching the engine or the
        // acct_* tables, so the operational expense-approval flow is byte-for-byte
        // unchanged and the existing expense tests pass unmodified.
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        // From here on FULLGL is ON. A posting failure SHOULD fail the operation
        // visibly (inline model, §2) — so once past the dark gate we let
        // PostingException propagate.
        await PostCoreAsync(expenseId, approvedByUserId, ct);
    }

    private async Task PostCoreAsync(int expenseId, int approvedByUserId, CancellationToken ct)
    {
        // Load the expense (incl. vendor) from the SHARED request-scoped context
        // (so the posting joins the caller's transaction).
        var expense = await db.Expenses
            .Include(e => e.Vendor)
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

        if (expense is null)
        {
            // The operational handler already loaded/updated it; if it's gone
            // here something is wrong, but don't invent a ledger entry.
            Log.Warning(
                "Expense posting skipped: expense {ExpenseId} not found when approving (FULLGL on).",
                expenseId);
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
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the expense into.");
        }

        // EntryDate = the expense date in the book's reporting context. DateOnly
        // is immune to UTC normalization (§5.1).
        var entryDate = DateOnly.FromDateTime(expense.ExpenseDate.UtcDateTime);

        var amount = expense.Amount;
        if (amount <= 0m)
        {
            // A zero/negative expense has nothing to post; skip rather than emit a
            // degenerate entry the balanced-check would reject anyway.
            Log.Information(
                "Expense posting skipped: expense {ExpenseId} has non-positive amount {Amount}.",
                expenseId, amount);
            return;
        }

        // ── Settlement disambiguation (§7 "Expense approved" / §6 Phase-1 row).
        // Explicit SettlementTarget wins; otherwise a present VendorId implies
        // AP (the expense is owed to that vendor), and the absence of either
        // means a directly-paid (cash) expense.
        var settlesToAp = expense.SettlementTarget switch
        {
            ExpenseSettlementTarget.AccountsPayable => true,
            ExpenseSettlementTarget.Cash => false,
            _ => expense.VendorId is not null, // null target → infer from vendor
        };

        var lines = new List<PostingLine>(2)
        {
            // Dr Expense for the full amount.
            new()
            {
                AccountKey = KeyOperatingExpense,
                JobId = expense.JobId, // carry the job tag when the expense is job-costed
                Debit = amount,
                Description = $"Expense EXP-{expense.Id} — {expense.Category}: {expense.Description}",
            },
        };

        if (settlesToAp)
        {
            // Cr AP, party = the vendor. AP is a control account → the engine
            // requires the vendor party here (§5.2). A vendor-settled expense
            // with no vendor is a misconfiguration we surface (FULLGL on only).
            if (expense.VendorId is null)
            {
                throw new PostingException(
                    "EXPENSE_AP_NO_VENDOR",
                    $"Expense EXP-{expense.Id} settles to Accounts Payable but has no Vendor to carry "
                    + "as the AP sub-ledger party (a control-account line requires a party — §5.2).");
            }

            lines.Add(new PostingLine
            {
                AccountKey = KeyApControl,
                PartyType = SubledgerPartyType.Vendor,
                PartyId = expense.VendorId.Value,
                Credit = amount,
                Description = $"AP — expense EXP-{expense.Id}"
                            + (expense.Vendor is not null ? $" ({expense.Vendor.CompanyName})" : string.Empty),
            });
        }
        else
        {
            // Cr Cash — directly paid expense (out-of-pocket / card / petty cash).
            lines.Add(new PostingLine
            {
                AccountKey = KeyCash,
                Credit = amount,
                Description = $"Cash — expense EXP-{expense.Id}",
            });
        }

        // ── Idempotency key (§5.2): source:type:id:purpose. AP/EXPENSE for an
        // expense; a re-approve returns the existing entry (no throw, no dup).
        var idempotencyKey = $"{JournalSource.AP}:Expense:{expense.Id}:EXPENSE";

        var request = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "Expense",
            SourceId = expense.Id,
            CurrencyId = book.FunctionalCurrencyId, // Phase-0/1 single-currency invariant
            Memo = $"Expense approved — EXP-{expense.Id}"
                 + (settlesToAp ? " (Cr AP)" : " (Cr Cash)"),
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };

        // ── Post inline on the shared context. The engine validates, assigns the
        // EntryNumber, maintains LedgerBalance, and calls SaveChangesAsync so the
        // write participates in the operational command's transaction (§2, §5.2).
        var entry = await postingEngine.PostAsync(request, approvedByUserId, ct);

        // ── Audit (§5.8): actor + before/after + reason on post. Best-effort —
        // an audit hiccup must not unwind a successful, committed posting.
        await TryAuditAsync(expense, entry, amount, settlesToAp, approvedByUserId, ct);
    }

    private async Task TryAuditAsync(
        Expense expense,
        JournalEntry entry,
        decimal amount,
        bool settlesToAp,
        int actorUserId,
        CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null, // an expense approval creates the entry; no prior ledger state
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    source = entry.Source.ToString(),
                    entryDate = entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount,
                    creditLeg = settlesToAp ? "AP_CONTROL" : "CASH",
                    vendorId = expense.VendorId,
                },
                reason = $"Expense EXP-{expense.Id} approved — expense posted ("
                       + (settlesToAp ? "Cr AP" : "Cr Cash") + ").",
            });

            await auditWriter.WriteAsync(
                action: "GlExpenseApPosted",
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
                "Expense posting audit write failed for expense {ExpenseId} (entry {EntryId}); posting itself is committed.",
                expense.Id, entry.Id);
        }
    }
}
