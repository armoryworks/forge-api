using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// The Forge native GL posting engine (§5.2). Validates a balanced
/// double-entry request and writes an immutable <see cref="JournalEntry"/> in
/// the caller's transaction, assigning the <c>EntryNumber</c> from a row-locked
/// counter and maintaining the incremental <see cref="LedgerBalance"/>
/// read-model. Phase-0 single-currency: every line's currency equals the
/// entry's, <c>FunctionalAmount = TxnAmount</c>, <c>FxRate = 1</c>.
/// <para>
/// Segregation of duties (§5.7) is enforced at this boundary via the optional
/// <see cref="IGlBoundaryAuthorizer"/>: <see cref="PostAsync"/> requires
/// <see cref="GlCapability.PostJournalEntry"/> and <see cref="ReverseAsync"/>
/// requires <see cref="GlCapability.ReverseJournalEntry"/>. When an authorizer
/// is injected (the production DI path) it denies callers who lack the
/// capability with a fail-safe default-deny. When the authorizer is
/// <c>null</c> — the explicit Phase-0 "dark" seam used by the engine's own unit
/// tests — the boundary is treated as not-yet-wired and posting proceeds
/// (CAP-ACCT-FULLGL is OFF so no command site reaches the engine in production).
/// </para>
/// </summary>
public sealed class ForgeGlPostingEngine(
    AppDbContext db,
    IAccountDeterminationResolver resolver,
    IAcctNumberSequenceAllocator sequenceAllocator,
    IClock clock,
    // TODO(§5.7 / Phase 1): make this non-optional once a command site wires the
    // engine. The null-default keeps the dark Phase-0 engine constructible from
    // unit tests that exercise posting mechanics without an identity context;
    // the production DI registration always supplies a real authorizer, whose
    // own default-deny is the fail-safe.
    IGlBoundaryAuthorizer? authorizer = null) : IPostingEngine
{
    public async Task<JournalEntry> PostAsync(
        PostingRequest request, int postedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // SoD boundary check (§5.7): a posting requires POST_JE in the caller's
        // effective capability set. Enforced only when the authorizer seam is
        // wired (see ctor note); the authorizer itself fail-safe-denies.
        authorizer?.EnsureAuthorized(GlCapability.PostJournalEntry);

        // --- Idempotency: a duplicate (BookId, IdempotencyKey) returns the
        // existing entry (no throw). Non-Manual sources MUST carry a key. ---
        if (request.Source != JournalSource.Manual && string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PostingException(
                "IDEMPOTENCY_KEY_REQUIRED",
                $"Source {request.Source} requires a non-null IdempotencyKey (shape source:type:id:purpose).");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await db.JournalEntries
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(
                    e => e.BookId == request.BookId && e.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
                return existing;
        }

        if (request.Lines is null || request.Lines.Count == 0)
            throw new PostingException("NO_LINES", "A posting request must have at least one line.");

        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId, ct)
            ?? throw new PostingException("BOOK_NOT_FOUND", $"Book {request.BookId} not found.");

        // --- Resolve + validate every line's account, build the persisted lines. ---
        var lines = new List<JournalLine>(request.Lines.Count);
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;
        var lineNumber = 0;

        foreach (var l in request.Lines)
        {
            lineNumber++;

            // Exactly one non-zero side, both non-negative.
            if (l.Debit < 0 || l.Credit < 0)
                throw new PostingException("NEGATIVE_AMOUNT", $"Line {lineNumber}: debit/credit must be >= 0.");
            if ((l.Debit == 0m) == (l.Credit == 0m))
                throw new PostingException(
                    "DEBIT_CREDIT_XOR",
                    $"Line {lineNumber}: exactly one of Debit/Credit must be non-zero (got Dr {l.Debit}, Cr {l.Credit}).");

            var account = await ResolveLineAccountAsync(request.BookId, l, lineNumber, ct);

            // Book-consistency: the account must belong to the entry's book.
            if (account.BookId != request.BookId)
                throw new PostingException(
                    "BOOK_MISMATCH_ACCOUNT",
                    $"Line {lineNumber}: account {account.AccountNumber} is in book {account.BookId}, not entry book {request.BookId}.");

            if (!account.IsPostable)
                throw new PostingException(
                    "ACCOUNT_NOT_POSTABLE",
                    $"Line {lineNumber}: account {account.AccountNumber} is not postable.");
            if (!account.IsActive)
                throw new PostingException(
                    "ACCOUNT_INACTIVE",
                    $"Line {lineNumber}: account {account.AccountNumber} is inactive.");

            // Control accounts post via a sub-ledger and require a party — EXCEPT inventory control
            // accounts, which are reconciled by PART via the valuation store (Phase-2 §8.1), not a
            // party sub-ledger (there is no inventory SubledgerPartyType), so they post party-less
            // (e.g. Cr INVENTORY_FG on the COGS relief). Stated as "everything except Inventory" so the
            // guard stays FAIL-SAFE: a misconfigured control account with a null ControlType still
            // demands a party rather than silently posting party-less.
            if (account.IsControlAccount
                && account.ControlType != ControlAccountType.Inventory
                && (l.PartyType is null || l.PartyId is null))
                throw new PostingException(
                    "CONTROL_LINE_PARTY_REQUIRED",
                    $"Line {lineNumber}: account {account.AccountNumber} is a control account and requires SubledgerPartyType/Id (only inventory control accounts post party-less).");

            // Dimension-required policy (§12): WIP/COGS accounts can require a Job; departmental accounts a
            // CostCenter. A line missing the required dimension is rejected.
            if (account.RequiresJob && l.JobId is null)
                throw new PostingException(
                    "JOB_REQUIRED",
                    $"Line {lineNumber}: account {account.AccountNumber} requires a Job dimension.");
            if (account.RequiresCostCenter && l.CostCenterId is null)
                throw new PostingException(
                    "COST_CENTER_REQUIRED",
                    $"Line {lineNumber}: account {account.AccountNumber} requires a CostCenter dimension.");

            // Book-consistency for the cost-center dimension.
            if (l.CostCenterId is int ccId)
            {
                var ccBookId = await db.CostCenters.AsNoTracking()
                    .Where(c => c.Id == ccId)
                    .Select(c => (int?)c.BookId)
                    .FirstOrDefaultAsync(ct);
                if (ccBookId is null)
                    throw new PostingException("COST_CENTER_NOT_FOUND", $"Line {lineNumber}: cost center {ccId} not found.");
                if (ccBookId != request.BookId)
                    throw new PostingException(
                        "BOOK_MISMATCH_COST_CENTER",
                        $"Line {lineNumber}: cost center {ccId} is in book {ccBookId}, not entry book {request.BookId}.");
            }

            var amount = l.Debit > 0 ? l.Debit : l.Credit;
            totalDebit += l.Debit;
            totalCredit += l.Credit;

            lines.Add(new JournalLine
            {
                BookId = request.BookId,
                LineNumber = lineNumber,
                GlAccountId = account.Id,
                JobId = l.JobId,
                CostCenterId = l.CostCenterId,
                Debit = l.Debit,
                Credit = l.Credit,
                CurrencyId = request.CurrencyId,       // Phase-0 single-currency invariant
                TxnAmount = amount,
                FunctionalAmount = amount,             // FunctionalAmount = TxnAmount
                FxRate = 1m,                           // pinned to 1 in Phase 0
                SubledgerPartyType = l.PartyType,
                SubledgerPartyId = l.PartyId,
                Description = l.Description,
            });
        }

        // --- Σ Debit == Σ Credit, within the book's rounding tolerance, but the
        // ledger row total must net to exactly 0.00. We require equality within
        // tolerance (handlers add an explicit ROUNDING line for allocations). ---
        var imbalance = Math.Abs(totalDebit - totalCredit);
        if (imbalance > book.RoundingTolerance)
            throw new PostingException(
                "UNBALANCED",
                $"Entry is unbalanced: Σdebit {totalDebit} vs Σcredit {totalCredit} (imbalance {imbalance} exceeds tolerance {book.RoundingTolerance}).");
        if (imbalance != 0m)
            throw new PostingException(
                "UNBALANCED_RESIDUAL",
                $"Entry has a {imbalance} residual within tolerance; add an explicit ROUNDING line so Σdebit == Σcredit exactly.");

        // --- Resolve the fiscal period from EntryDate under a row lock; reject
        // HardClosed; block SoftClosed unless an audited override is supplied. ---
        var period = await ResolveAndLockPeriodAsync(request.BookId, request.EntryDate, ct);

        if (period.Status == FiscalPeriodStatus.HardClosed)
            throw new PostingException(
                "PERIOD_HARD_CLOSED",
                $"Cannot post into hard-closed period '{period.Name}' (EntryDate {request.EntryDate}).");
        if (period.Status == FiscalPeriodStatus.SoftClosed && !request.AllowSoftClosedOverride)
            throw new PostingException(
                "PERIOD_SOFT_CLOSED",
                $"Period '{period.Name}' is soft-closed; a controller override is required to post into it.");

        var entry = await BuildAndAddEntryAsync(
            request, lines, period, postedByUserId, ct,
            source: request.Source,
            sourceType: request.SourceType,
            sourceId: request.SourceId,
            idempotencyKey: request.IdempotencyKey,
            memo: request.Memo,
            autoReverse: request.AutoReverseNextPeriod,
            reversalOfEntryId: null);

        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<JournalEntry> ReverseAsync(
        long entryId,
        DateOnly reversalDate,
        string reason,
        int reversedByUserId,
        CancellationToken ct = default)
    {
        // SoD boundary check (§5.7): reversal requires REVERSE_JE. Enforced only
        // when the authorizer seam is wired (see ctor note).
        authorizer?.EnsureAuthorized(GlCapability.ReverseJournalEntry);

        var original = await db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new PostingException("ENTRY_NOT_FOUND", $"Journal entry {entryId} not found.");

        // --- Preconditions: Posted AND not already reversed (no double-reverse).
        // Check already-reversed first so a re-reverse reports the precise reason
        // (the original is left Reversed by the prior reversal, so the status
        // check alone would mask it). ---
        // Reversal-of-reversal policy (§12): a reversal entry is itself Posted with a null ReversedByEntryId,
        // so it MAY be reversed in turn — that re-instates the original economically (a correction of a
        // correction). The same two preconditions apply uniformly; no special-casing for reversal entries.
        if (original.ReversedByEntryId is not null)
            throw new PostingException(
                "ALREADY_REVERSED",
                $"Entry {entryId} is already reversed by entry {original.ReversedByEntryId}.");
        if (original.Status != JournalEntryStatus.Posted)
            throw new PostingException(
                "REVERSE_NOT_POSTED",
                $"Only a Posted entry can be reversed; entry {entryId} is {original.Status}.");

        // Idempotency: a reversal carries a REVERSAL-purpose key; a duplicate
        // reversal request returns the existing reversal.
        var idempotencyKey = $"{original.Source}:JournalEntry:{original.Id}:REVERSAL";
        var existingReversal = await db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(
                e => e.BookId == original.BookId && e.IdempotencyKey == idempotencyKey, ct);
        if (existingReversal is not null)
            return existingReversal;

        // --- Resolve the reversal's own period from its own date; reject HardClosed. ---
        var period = await ResolveAndLockPeriodAsync(original.BookId, reversalDate, ct);
        if (period.Status == FiscalPeriodStatus.HardClosed)
            throw new PostingException(
                "PERIOD_HARD_CLOSED",
                $"Cannot post a reversal into hard-closed period '{period.Name}' (date {reversalDate}).");

        // Equal-and-opposite lines (Dr<->Cr swapped), preserving dimensions/party.
        var reversalLines = original.Lines
            .OrderBy(l => l.LineNumber)
            .Select((l, i) => new JournalLine
            {
                BookId = original.BookId,
                LineNumber = i + 1,
                GlAccountId = l.GlAccountId,
                JobId = l.JobId,
                CostCenterId = l.CostCenterId,
                Debit = l.Credit,
                Credit = l.Debit,
                CurrencyId = l.CurrencyId,
                TxnAmount = l.TxnAmount,
                FunctionalAmount = l.FunctionalAmount,
                FxRate = l.FxRate,
                SubledgerPartyType = l.SubledgerPartyType,
                SubledgerPartyId = l.SubledgerPartyId,
                Description = l.Description,
            })
            .ToList();

        var reversal = await BuildAndAddEntryAsync(
            new PostingRequest
            {
                BookId = original.BookId,
                EntryDate = reversalDate,
                CurrencyId = original.CurrencyId,
            },
            reversalLines, period, reversedByUserId, ct,
            source: original.Source,
            sourceType: "JournalEntry",
            sourceId: original.Id,
            idempotencyKey: idempotencyKey,
            memo: $"Reversal of entry {original.EntryNumber}: {reason}",
            autoReverse: false,
            reversalOfEntryId: original.Id);

        // --- The sole permitted mutation on a Posted row: Posted→Reversed + link. ---
        original.Status = JournalEntryStatus.Reversed;
        original.ReversedByEntryId = reversal.Id;

        await db.SaveChangesAsync(ct);
        return reversal;
    }

    private async Task<GlAccount> ResolveLineAccountAsync(
        int bookId, PostingLine l, int lineNumber, CancellationToken ct)
    {
        var hasKey = !string.IsNullOrWhiteSpace(l.AccountKey);
        var hasId = l.GlAccountId is not null;
        if (hasKey == hasId)
            throw new PostingException(
                "LINE_ACCOUNT_AMBIGUOUS",
                $"Line {lineNumber}: supply exactly one of AccountKey or GlAccountId.");

        if (hasKey)
            return await resolver.ResolveAsync(bookId, l.AccountKey!, ct);

        var account = await db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == l.GlAccountId!.Value, ct)
            ?? throw new PostingException("ACCOUNT_NOT_FOUND", $"Line {lineNumber}: account {l.GlAccountId} not found.");
        return account;
    }

    /// <summary>
    /// Builds a Posted journal entry header from resolved lines, allocates its
    /// EntryNumber via the row-locked counter, adds it to the context (NOT yet
    /// saved — the caller saves so the write joins their transaction), and
    /// folds the lines into the incremental <see cref="LedgerBalance"/>.
    /// </summary>
    private async Task<JournalEntry> BuildAndAddEntryAsync(
        PostingRequest request,
        List<JournalLine> lines,
        FiscalPeriod period,
        int postedByUserId,
        CancellationToken ct,
        JournalSource source,
        string? sourceType,
        long? sourceId,
        string? idempotencyKey,
        string? memo,
        bool autoReverse,
        long? reversalOfEntryId)
    {
        var fiscalYearId = period.FiscalYear?.Id ?? await db.FiscalPeriods
            .Where(p => p.Id == period.Id)
            .Select(p => p.FiscalYearId)
            .FirstAsync(ct);

        var entryNumber = await sequenceAllocator.AllocateNextAsync(request.BookId, fiscalYearId, ct);

        var entry = new JournalEntry
        {
            BookId = request.BookId,
            EntryNumber = entryNumber,
            EntryDate = request.EntryDate,
            FiscalPeriodId = period.Id,
            FiscalYearId = fiscalYearId,
            Source = source,
            SourceType = sourceType,
            SourceId = sourceId,
            IdempotencyKey = idempotencyKey,
            CurrencyId = request.CurrencyId,
            Memo = memo,
            Status = JournalEntryStatus.Posted,
            AutoReverseNextPeriod = autoReverse,
            ReversalOfEntryId = reversalOfEntryId,
            PostedBy = postedByUserId,
            ApprovedBy = request.ApprovedByUserId, // maker-checker second approver (§5.7), when supplied
            PostedAt = clock.UtcNow,
            Lines = lines,
        };

        db.JournalEntries.Add(entry);

        await ApplyToLedgerBalancesAsync(entry, period.Id, ct);
        return entry;
    }

    /// <summary>
    /// Incrementally maintains <see cref="LedgerBalance"/> at grain
    /// (BookId, GlAccountId, FiscalPeriodId, CurrencyId) inside the posting
    /// transaction. A reversal is just another posting (Dr/Cr swapped) — no
    /// special case (§5.1).
    /// </summary>
    private async Task ApplyToLedgerBalancesAsync(JournalEntry entry, int fiscalPeriodId, CancellationToken ct)
    {
        // Aggregate this entry's lines per (account, currency) first to minimize
        // balance-row touches.
        var grouped = entry.Lines
            .GroupBy(l => new { l.GlAccountId, l.CurrencyId })
            .Select(g => new
            {
                g.Key.GlAccountId,
                g.Key.CurrencyId,
                Debit = g.Sum(x => x.FunctionalAmount * (x.Debit > 0 ? 1 : 0)),
                Credit = g.Sum(x => x.FunctionalAmount * (x.Credit > 0 ? 1 : 0)),
            });

        foreach (var g in grouped)
        {
            var balance = await db.LedgerBalances.FirstOrDefaultAsync(
                b => b.BookId == entry.BookId
                  && b.GlAccountId == g.GlAccountId
                  && b.FiscalPeriodId == fiscalPeriodId
                  && b.CurrencyId == g.CurrencyId, ct);

            if (balance is null)
            {
                balance = new LedgerBalance
                {
                    BookId = entry.BookId,
                    GlAccountId = g.GlAccountId,
                    FiscalPeriodId = fiscalPeriodId,
                    CurrencyId = g.CurrencyId,
                    DebitTotal = g.Debit,
                    CreditTotal = g.Credit,
                };
                db.LedgerBalances.Add(balance);
            }
            else
            {
                balance.DebitTotal += g.Debit;
                balance.CreditTotal += g.Credit;
            }
        }
    }

    /// <summary>
    /// Finds the fiscal period whose [StartDate, EndDate] contains
    /// <paramref name="entryDate"/> for the book, and takes a row lock on it
    /// (guarding the close-vs-post race — §5.1, §9). On Npgsql the lock is a
    /// real <c>FOR UPDATE</c>; on non-relational providers (InMemory tests) the
    /// lock is a no-op and the tracked entity is returned.
    /// </summary>
    private async Task<FiscalPeriod> ResolveAndLockPeriodAsync(
        int bookId, DateOnly entryDate, CancellationToken ct)
    {
        var period = await db.FiscalPeriods
            .Include(p => p.FiscalYear)
            .FirstOrDefaultAsync(p =>
                p.FiscalYear.BookId == bookId
                && p.StartDate <= entryDate
                && p.EndDate >= entryDate, ct)
            ?? throw new PostingException(
                "PERIOD_NOT_FOUND",
                $"No fiscal period covers EntryDate {entryDate} for book {bookId}.");

        if (db.Database.IsNpgsql())
        {
            // Re-read the located row under FOR UPDATE so a concurrent close
            // blocks until we commit (and we observe its committed status).
            await db.Database.ExecuteSqlRawAsync(
                "SELECT id FROM acct_fiscal_periods WHERE id = {0} FOR UPDATE", [period.Id], ct);
            await db.Entry(period).ReloadAsync(ct);
        }

        return period;
    }
}
