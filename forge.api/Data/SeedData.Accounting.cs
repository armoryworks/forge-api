using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    // ── Accounting / GL Phase-0 seed (ACCOUNTING_SUITE_PLAN §5.4) ───────────
    //
    // Seeds a single default Book (functional currency = base currency), a
    // small-manufacturer chart of accounts (incl. control accounts), an
    // AccountDeterminationRule row pointing each seeded Key at its account, and
    // the current FiscalYear + 12 FiscalPeriods (plus the per-(book, year)
    // EntryNumber counter).
    //
    // DARK BY DESIGN: this only writes config/reference rows. It does NOT enable
    // CAP-ACCT-FULLGL (still default-off in the capability catalog and unwired),
    // so the GL stays dark — nothing posts, nothing reads these tables until the
    // capability is turned on in a later phase.
    //
    // Idempotent / runs once: guarded on `acct_books` being empty. Re-running the
    // seed is a no-op once a Book exists; tenant edits are never overwritten.

    internal static async Task SeedAccountingAsync(AppDbContext db)
    {
        // Additive chart patch — runs on EVERY boot, before the run-once guard
        // below returns, so installs seeded before the patched account existed
        // get it backfilled. No-op on fresh installs and already-patched ones.
        await EnsureCashInTransitAsync(db);

        // AR-002/AP-001 open-item sub-ledger backfill — runs on EVERY boot, before
        // the run-once guard, so installs that posted AR/AP journals before the
        // open-item tables existed get their items reconstructed. No-op on fresh
        // installs and on any install where items already exist.
        await EnsureOpenItemsBackfilledAsync(db);

        // Expense→bill promotion backfill — runs on EVERY boot, after the open-item
        // backfill. Vendor-settled expenses posted under the legacy Expense-keyed AP
        // origination (before promotion existed) get a linked, born-Approved
        // VendorBill + ApOpenItem reconstructed (no new GL — the journal already
        // exists), so they become payable/agable and the AP reconciliation ties.
        // Idempotent per expense: skipped once a bill references it.
        await EnsureExpenseBillsBackfilledAsync(db);

        // Run-once guard: if any Book exists the GL foundation is already seeded.
        if (await db.Books.AnyAsync())
        {
            Log.Debug("Accounting GL seed skipped — a Book already exists (idempotent).");
            return;
        }

        // ── Functional currency = base currency ──────────────────────────────
        // The Currency entity table is not seeded elsewhere (only the `currency`
        // ReferenceData group is). Book.FunctionalCurrencyId is a required FK to
        // Currency, so ensure a base currency row exists first.
        var baseCurrency = await db.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency)
            ?? await db.Currencies.FirstOrDefaultAsync(c => c.Code == "USD");

        if (baseCurrency is null)
        {
            baseCurrency = new Currency
            {
                Code = "USD",
                Name = "US Dollar",
                Symbol = "$",
                DecimalPlaces = 2,
                IsBaseCurrency = true,
                IsActive = true,
                SortOrder = 1,
            };
            db.Currencies.Add(baseCurrency);
            await db.SaveChangesAsync();
            Log.Information("Seeded base currency {Code} for accounting GL", baseCurrency.Code);
        }

        // ── Default Book ─────────────────────────────────────────────────────
        var book = new Book
        {
            Code = "MAIN",
            Name = "Default Book",
            FunctionalCurrencyId = baseCurrency.Id,
            ReportingTimeZone = "America/New_York",
            // Smallest unit of the functional currency (e.g. 0.01 for USD).
            RoundingTolerance = baseCurrency.DecimalPlaces > 0
                ? 1m / (decimal)Math.Pow(10, baseCurrency.DecimalPlaces)
                : 1m,
            IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        // ── Chart of accounts (small manufacturer) ───────────────────────────
        // (AccountNumber, Name, AccountType, NormalBalance, IsControl, ControlType,
        //  DeterminationKey?). Numbers follow a conventional block layout:
        //   1xxxx Assets, 2xxxx Liabilities, 3xxxx Equity, 4xxxx Income,
        //   5xxxx COGS, 6xxxx Expense, 7xxxx/9xxxx Other income/expense.
        // Control accounts (AR, AP, Inventory) post ONLY via sub-ledgers (§5.1).
        var defs = new (string Number, string Name, AccountType Type, NormalBalance Normal,
            bool IsControl, ControlAccountType? Control, string? Key)[]
        {
            // ── Assets (1xxxx) ──────────────────────────────────────────────
            ("10100", "Cash — Operating",            AccountType.Asset,     NormalBalance.Debit,  false, null,                         "CASH"),
            // §7 BANK-002: clearing account decoupling payment INTENT (origination) from CONFIRMED
            // settlement. Electronic disbursements credit this instead of CASH; the transmission-success
            // settlement entry (Dr CASH_IN_TRANSIT / Cr CASH) clears it.
            ("10150", "Cash in Transit",             AccountType.Asset,     NormalBalance.Debit,  false, null,                         "CASH_IN_TRANSIT"),
            ("11000", "Accounts Receivable",         AccountType.Asset,     NormalBalance.Debit,  true,  ControlAccountType.AR,        "AR_CONTROL"),
            ("12000", "Prepaid Expenses",            AccountType.Asset,     NormalBalance.Debit,  false, null,                         "PREPAID_EXPENSE"),
            ("13100", "Inventory — Raw Materials",   AccountType.Asset,     NormalBalance.Debit,  true,  ControlAccountType.Inventory, "INVENTORY_RAW"),
            ("13200", "Inventory — Work in Process", AccountType.Asset,     NormalBalance.Debit,  true,  ControlAccountType.Inventory, "INVENTORY_WIP"),
            ("13250", "Inventory — Subassemblies",   AccountType.Asset,     NormalBalance.Debit,  true,  ControlAccountType.Inventory, "INVENTORY_SUBASSEMBLY"),
            ("13300", "Inventory — Finished Goods",  AccountType.Asset,     NormalBalance.Debit,  true,  ControlAccountType.Inventory, "INVENTORY_FG"),
            // Standard costing: actual overhead accumulates here (Dr) and is relieved as applied (Cr); the
            // residual balance is the over/under-applied overhead (the spending + volume variance) at period end.
            ("13400", "Manufacturing Overhead Control", AccountType.Asset,  NormalBalance.Debit,  false, null,                         "OVERHEAD_CONTROL"),

            // ── Liabilities (2xxxx) ─────────────────────────────────────────
            ("20000", "Accounts Payable",            AccountType.Liability, NormalBalance.Credit, true,  ControlAccountType.AP,        "AP_CONTROL"),
            ("21000", "Goods Received Not Invoiced", AccountType.Liability, NormalBalance.Credit, false, null,                         "GRNI"),
            ("22000", "Freight Clearing",            AccountType.Liability, NormalBalance.Credit, false, null,                         "FREIGHT_CLEARING"),
            ("23000", "Sales Tax Payable",           AccountType.Liability, NormalBalance.Credit, false, null,                         "SALES_TAX_PAYABLE"),
            ("24000", "Deferred Revenue",            AccountType.Liability, NormalBalance.Credit, false, null,                         "DEFERRED_REVENUE"),
            ("24100", "Unbilled Revenue",            AccountType.Liability, NormalBalance.Credit, false, null,                         "UNBILLED_REVENUE"),
            ("24500", "Customer Deposits",           AccountType.Liability, NormalBalance.Credit, false, null,                         "CUSTOMER_DEPOSITS"),
            ("25000", "Refunds Payable",             AccountType.Liability, NormalBalance.Credit, false, null,                         "REFUNDS_PAYABLE"),
            ("26000", "Accrued Expenses",            AccountType.Liability, NormalBalance.Credit, false, null,                         "ACCRUED_EXPENSE"),
            ("26100", "Accrued Wages",               AccountType.Liability, NormalBalance.Credit, false, null,                         "ACCRUED_WAGES"),

            // ── Equity (3xxxx) ──────────────────────────────────────────────
            ("30000", "Retained Earnings",           AccountType.Equity,    NormalBalance.Credit, false, null,                         "RETAINED_EARNINGS"),
            ("39990", "Cumulative Translation Adj.", AccountType.Equity,    NormalBalance.Credit, false, null,                         "CTA"),
            ("39999", "Rounding",                    AccountType.Equity,    NormalBalance.Credit, false, null,                         "ROUNDING"),

            // ── Income (4xxxx) ──────────────────────────────────────────────
            ("40000", "Sales Revenue",               AccountType.Income,    NormalBalance.Credit, false, null,                         "SALES_REVENUE"),
            ("41000", "Sales Returns & Allowances",  AccountType.Income,    NormalBalance.Debit,  false, null,                         "SALES_RETURNS"),

            // ── COGS (5xxxx) ────────────────────────────────────────────────
            ("50000", "Cost of Goods Sold",          AccountType.Expense,   NormalBalance.Debit,  false, null,                         "COGS"),
            ("51000", "Purchase Price Variance",     AccountType.Expense,   NormalBalance.Debit,  false, null,                         "PURCHASE_PRICE_VARIANCE"),
            ("51100", "Material Usage Variance",     AccountType.Expense,   NormalBalance.Debit,  false, null,                         "MATERIAL_USAGE_VARIANCE"),
            ("51200", "Production Variance",         AccountType.Expense,   NormalBalance.Debit,  false, null,                         "PRODUCTION_VARIANCE"),
            // Absorption clearing (contra-expense, credit-normal): labor/overhead capitalized into WIP. At
            // period end these net against actual wages (WAGE_EXPENSE) + actual overhead to give the
            // over/under-absorbed labor/overhead variance.
            ("51210", "Labor Absorbed",              AccountType.Expense,   NormalBalance.Credit, false, null,                         "LABOR_APPLIED"),
            ("51220", "Overhead Absorbed",           AccountType.Expense,   NormalBalance.Credit, false, null,                         "OVERHEAD_APPLIED"),
            ("51230", "Subcontract Absorbed",        AccountType.Expense,   NormalBalance.Credit, false, null,                         "SUBCONTRACT_APPLIED"),
            // Standard-cost variance decomposition (the 6 slots; material price reuses PURCHASE_PRICE_VARIANCE
            // 51000, material usage reuses MATERIAL_USAGE_VARIANCE 51100). Debit = unfavorable, credit = favorable.
            ("51300", "Labor Rate Variance",         AccountType.Expense,   NormalBalance.Debit,  false, null,                         "LABOR_RATE_VARIANCE"),
            ("51310", "Labor Efficiency Variance",   AccountType.Expense,   NormalBalance.Debit,  false, null,                         "LABOR_EFFICIENCY_VARIANCE"),
            ("51320", "Overhead Spending Variance",  AccountType.Expense,   NormalBalance.Debit,  false, null,                         "OVERHEAD_SPENDING_VARIANCE"),
            ("51330", "Overhead Efficiency Variance",AccountType.Expense,   NormalBalance.Debit,  false, null,                         "OVERHEAD_EFFICIENCY_VARIANCE"),
            ("52000", "Inventory Write-Down",        AccountType.Expense,   NormalBalance.Debit,  false, null,                         "INVENTORY_WRITEDOWN"),

            // ── Operating expense (6xxxx) ───────────────────────────────────
            // Phase-1 STAGE C — the default debit target for an approved Expense
            // (§7 "Expense approved": Dr Expense / Cr AP-or-Cash). Per-category
            // determination is deferred (§12); Phase 1 routes all expense
            // categories to this single operating-expense account.
            ("60000", "General & Administrative",    AccountType.Expense,   NormalBalance.Debit,  false, null,                         "OPERATING_EXPENSE"),

            // ── Other income / expense (9xxxx) ──────────────────────────────
            ("90000", "Foreign Exchange Gain",       AccountType.Income,    NormalBalance.Credit, false, null,                         "FX_GAIN"),
            ("90100", "Foreign Exchange Loss",       AccountType.Expense,   NormalBalance.Debit,  false, null,                         "FX_LOSS"),
        };

        var accountsByKey = new Dictionary<string, GlAccount>(StringComparer.Ordinal);
        var accounts = new List<GlAccount>(defs.Length);
        foreach (var d in defs)
        {
            var acct = new GlAccount
            {
                BookId = book.Id,
                AccountNumber = d.Number,
                Name = d.Name,
                AccountType = d.Type,
                NormalBalance = d.Normal,
                IsControlAccount = d.IsControl,
                ControlType = d.Control,
                IsPostable = true,
                IsActive = true,
            };
            accounts.Add(acct);
            if (d.Key is not null)
                accountsByKey[d.Key] = acct;
        }
        db.GlAccounts.AddRange(accounts);
        await db.SaveChangesAsync();

        // ── Account-determination rules (global scope; one per seeded Key) ───
        // Phase 0 seeds only global rows (all scope columns null). The
        // determination map resolves (BookId, Key) → account so business events
        // never hardcode accounts (§5.1).
        var rules = accountsByKey.Select(kv => new AccountDeterminationRule
        {
            BookId = book.Id,
            Key = kv.Key,
            GlAccountId = kv.Value.Id,
        }).ToList();
        db.AccountDeterminationRules.AddRange(rules);

        // ── Current fiscal year + 12 monthly periods ─────────────────────────
        var currentYear = DateTimeOffset.UtcNow.Year;
        var fiscalYear = new FiscalYear
        {
            BookId = book.Id,
            Name = currentYear.ToString(),
            StartDate = new DateOnly(currentYear, 1, 1),
            EndDate = new DateOnly(currentYear, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        for (int month = 1; month <= 12; month++)
        {
            var start = new DateOnly(currentYear, month, 1);
            var end = start.AddMonths(1).AddDays(-1); // last day of the month
            fiscalYear.Periods.Add(new FiscalPeriod
            {
                PeriodNumber = month,
                Name = start.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                StartDate = start,
                EndDate = end,
                Status = FiscalPeriodStatus.Open,
            });
        }
        db.FiscalYears.Add(fiscalYear);
        await db.SaveChangesAsync();

        // ── EntryNumber counter for (book, year) ─────────────────────────────
        // Mirrors the run-time allocation grain; EntryNumber starts at 1.
        db.AcctNumberSequences.Add(new AcctNumberSequence
        {
            BookId = book.Id,
            FiscalYearId = fiscalYear.Id,
            NextValue = 1,
        });
        await db.SaveChangesAsync();

        Log.Information(
            "Seeded accounting GL foundation: Book '{Book}' (currency {Currency}), {Accounts} accounts, " +
            "{Rules} determination rules, FiscalYear {Year} + {Periods} periods (CAP-ACCT-FULLGL remains off — dark).",
            book.Code, baseCurrency.Code, accounts.Count, rules.Count, fiscalYear.Name, fiscalYear.Periods.Count);
    }

    // ── Additive chart patch: 10150 "Cash in Transit" (§7 BANK-002) ─────────
    //
    // PATTERN for future additive chart entries on pre-seeded installs: the
    // main seed above is run-once (guarded on acct_books being non-empty), so
    // an account added to the seeded chart AFTER an install was first seeded
    // never arrives there — it used to require a hand INSERT. Each such
    // account gets a small Ensure*Async step like this one, called from
    // SeedAccountingAsync BEFORE the run-once guard so it executes on every
    // boot. Keyed on the AccountDeterminationRule (the stable lookup the
    // posting engine resolves through): rule present → no-op; rule absent →
    // insert the GlAccount (reusing a hand-inserted one when present) + the
    // rule, per active Book. Idempotent; never mutates existing rows.
    // ── AR-002/AP-001: open-item sub-ledger backfill (boot-time ensure) ──────
    //
    // The open items are normally created/maintained by the posting services at
    // posting time, but installs that enabled CAP-ACCT-FULLGL BEFORE the
    // open-item tables existed have posted AR/AP origination journals with no
    // items. Mirrors the EnsureCashInTransitAsync pattern: runs on every boot
    // before the run-once guard; per side (AR / AP) it backfills ONLY when that
    // open-item table is EMPTY (the idempotency guard — once any item exists,
    // posting-time maintenance owns the table and the backfill never re-runs).
    //
    // Reconstruction matches the posting-time math exactly so the reconciliation
    // ties: items are built from operational Invoices / VendorBills that have a
    // POSTED origination journal (matched by the posting idempotency keys
    // AR:Invoice:{id}:REVENUE / AP:VendorBill:{id}:BILL); applied amounts come
    // from their Payment/VendorPayment applications — counted only when the
    // payment's own origination journal is Posted (a payment recorded while
    // FULLGL was off moved no control balance, so it must not shrink the item) —
    // functional at the DOCUMENT's booking rate; statuses recomputed. A vendor
    // bill whose origination is Reversed (voided bill) gets a Voided item,
    // matching the posting-time void path.
    internal static async Task EnsureOpenItemsBackfilledAsync(AppDbContext db)
    {
        var book = await db.Books.Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync();
        if (book is null)
            return; // No GL seeded — nothing was ever posted.

        // ── AR side ──────────────────────────────────────────────────────────
        if (!await db.ArOpenItems.AnyAsync())
        {
            // Posted invoice originations (the Dr AR_CONTROL revenue entries).
            var postedInvoiceIds = (await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == book.Id
                    && e.Source == JournalSource.AR
                    && e.SourceType == "Invoice"
                    && e.SourceId != null
                    && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(":REVENUE")
                    && e.Status == JournalEntryStatus.Posted)
                .Select(e => e.SourceId!.Value)
                .ToListAsync()).ToHashSet();

            if (postedInvoiceIds.Count > 0)
            {
                // Payments whose cash-receipt origination is Posted — only these
                // relieved AR control, so only these count as applied. (A voided
                // payment's origination is Reversed AND its applications are
                // removed; a dark-mode payment never posted.)
                var postedPaymentIds = (await db.JournalEntries.IgnoreQueryFilters()
                    .Where(e => e.BookId == book.Id
                        && e.Source == JournalSource.AR
                        && e.SourceType == "Payment"
                        && e.SourceId != null
                        && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(":PAYMENT")
                        && e.Status == JournalEntryStatus.Posted)
                    .Select(e => e.SourceId!.Value)
                    .ToListAsync()).ToHashSet();

                // Bounded by the posted originations (not the whole invoice table).
                var invoices = await db.Invoices.IgnoreQueryFilters()
                    .Include(i => i.Lines)
                    .Include(i => i.Customer)
                    .Include(i => i.PaymentApplications)
                    .Where(i => postedInvoiceIds.Contains((long)i.Id))
                    .ToListAsync();

                foreach (var invoice in invoices)
                {
                    // Mirror InvoiceArPostingService: posted total = subtotal + non-exempt tax.
                    var subtotal = invoice.Lines.Sum(l => l.LineTotal);
                    var taxAmount = invoice.Customer.IsTaxExempt ? 0m : subtotal * invoice.TaxRate;
                    var arTotal = subtotal + taxAmount;
                    if (arTotal <= 0m)
                        continue; // the posting skipped degenerate totals; mirror it defensively

                    var item = new ArOpenItem
                    {
                        BookId = book.Id,
                        CustomerId = invoice.CustomerId,
                        SourceType = "Invoice",
                        SourceId = invoice.Id,
                        DocumentNumber = invoice.InvoiceNumber,
                        DocumentDate = invoice.InvoiceDate,
                        DueDate = invoice.DueDate,
                        CurrencyId = invoice.CurrencyId,
                        FxRate = invoice.FxRate,
                        OriginalTxnAmount = arTotal,
                        OriginalFunctionalAmount = Math.Round(arTotal * invoice.FxRate, 2, MidpointRounding.AwayFromZero),
                    };

                    foreach (var app in invoice.PaymentApplications.Where(a => postedPaymentIds.Contains(a.PaymentId)))
                    {
                        item.AppliedTxnAmount += app.Amount;
                        item.AppliedFunctionalAmount +=
                            Math.Round(app.Amount * invoice.FxRate, 2, MidpointRounding.AwayFromZero);
                    }

                    item.RecomputeStatus();
                    db.ArOpenItems.Add(item);
                }

                await db.SaveChangesAsync();
                Log.Information(
                    "Backfilled {Count} AR open items from posted invoice originations (open-item sub-ledger boot ensure).",
                    invoices.Count);
            }
        }

        // ── AP side (mirror) ─────────────────────────────────────────────────
        if (!await db.ApOpenItems.AnyAsync())
        {
            // Bill originations: Posted → active item; Reversed → the bill was
            // voided (its GL nets to zero) → Voided item, like the void path.
            var billOriginations = await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == book.Id
                    && e.Source == JournalSource.AP
                    && e.SourceType == "VendorBill"
                    && e.SourceId != null
                    && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(":BILL")
                    && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed))
                .Select(e => new { SourceId = e.SourceId!.Value, e.Status })
                .ToListAsync();

            if (billOriginations.Count > 0)
            {
                var voidedBillIds = billOriginations
                    .Where(o => o.Status == JournalEntryStatus.Reversed)
                    .Select(o => o.SourceId)
                    .ToHashSet();
                var billIds = billOriginations.Select(o => o.SourceId).ToHashSet();

                var postedVendorPaymentIds = (await db.JournalEntries.IgnoreQueryFilters()
                    .Where(e => e.BookId == book.Id
                        && e.Source == JournalSource.AP
                        && e.SourceType == "VendorPayment"
                        && e.SourceId != null
                        && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(":PAYMENT")
                        && e.Status == JournalEntryStatus.Posted)
                    .Select(e => e.SourceId!.Value)
                    .ToListAsync()).ToHashSet();

                var bills = await db.VendorBills.IgnoreQueryFilters()
                    .Include(b => b.Lines)
                    .Include(b => b.PaymentApplications)
                    .Where(b => billIds.Contains((long)b.Id))
                    .ToListAsync();

                foreach (var bill in bills)
                {
                    var total = bill.Total; // Σ line totals + tax — exactly what the posting credited
                    if (total <= 0m)
                        continue;

                    var item = new ApOpenItem
                    {
                        BookId = book.Id,
                        VendorId = bill.VendorId,
                        SourceType = "VendorBill",
                        SourceId = bill.Id,
                        DocumentNumber = bill.BillNumber,
                        DocumentDate = bill.BillDate,
                        DueDate = bill.DueDate,
                        CurrencyId = bill.CurrencyId,
                        FxRate = bill.FxRate,
                        OriginalTxnAmount = total,
                        OriginalFunctionalAmount = Math.Round(total * bill.FxRate, 2, MidpointRounding.AwayFromZero),
                    };

                    foreach (var app in bill.PaymentApplications.Where(a => postedVendorPaymentIds.Contains(a.VendorPaymentId)))
                    {
                        item.AppliedTxnAmount += app.Amount;
                        item.AppliedFunctionalAmount +=
                            Math.Round(app.Amount * bill.FxRate, 2, MidpointRounding.AwayFromZero);
                    }

                    item.RecomputeStatus();
                    if (voidedBillIds.Contains(bill.Id))
                        item.Status = OpenItemStatus.Voided;

                    db.ApOpenItems.Add(item);
                }

                await db.SaveChangesAsync();
                Log.Information(
                    "Backfilled {Count} AP open items from posted vendor-bill originations (open-item sub-ledger boot ensure).",
                    bills.Count);
            }
        }
    }

    // ── Expense→bill promotion backfill (boot ensure) ────────────────────────
    //
    // Before promotion existed, approving a vendor-settled expense (FULLGL on)
    // posted Dr Expense / Cr AP under the legacy AP:Expense:{id}:EXPENSE key with
    // NO bill and NO open item — a payable nothing in the app could relieve, and
    // a standing control-vs-subledger reconciliation difference. For each such
    // expense this reconstructs a linked, born-Approved VendorBill plus its
    // ApOpenItem, WITHOUT posting (the journal already exists; the bill's void
    // path knows to reverse the legacy-keyed origination via VendorBill.ExpenseId).
    //
    // Idempotent per expense: skipped once ANY bill references the expense. The
    // amount/vendor are taken from the posted AP credit line (ground truth at
    // posting time), not the mutable expense row. Dark installs are untouched —
    // no posted origination, no bill (auto-creating payables for possibly
    // already-paid-out-of-band history would risk double payment).
    internal static async Task EnsureExpenseBillsBackfilledAsync(AppDbContext db)
    {
        var book = await db.Books.Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync();
        if (book is null)
            return; // No GL seeded — nothing was ever posted.

        // Posted legacy expense AP originations, with the vendor + posted amount from the AP credit
        // line (the shared :EXPENSE key also covers the cash-settled variant, which has no party line).
        var originations = await db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.BookId == book.Id
                && e.Source == JournalSource.AP
                && e.SourceType == "Expense"
                && e.SourceId != null
                && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(":EXPENSE")
                && e.Status == JournalEntryStatus.Posted)
            .SelectMany(e => e.Lines
                .Where(l => l.SubledgerPartyType == Forge.Core.Enums.Accounting.SubledgerPartyType.Vendor
                    && l.SubledgerPartyId != null
                    && l.Credit > 0m)
                .Select(l => new { ExpenseId = (int)e.SourceId!.Value, VendorId = l.SubledgerPartyId!.Value, Amount = l.Credit }))
            .ToListAsync();

        if (originations.Count == 0)
            return;

        var expenseIds = originations.Select(o => o.ExpenseId).ToList();
        var alreadyPromoted = (await db.VendorBills.IgnoreQueryFilters()
            .Where(b => b.ExpenseId != null && expenseIds.Contains(b.ExpenseId.Value))
            .Select(b => b.ExpenseId!.Value)
            .ToListAsync()).ToHashSet();

        var pending = originations.Where(o => !alreadyPromoted.Contains(o.ExpenseId)).ToList();
        if (pending.Count == 0)
            return;

        var expenses = await db.Expenses.IgnoreQueryFilters()
            .Where(e => expenseIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var created = 0;
        foreach (var o in pending)
        {
            expenses.TryGetValue(o.ExpenseId, out var expense);
            var billDate = expense?.ExpenseDate ?? DateTimeOffset.UtcNow;

            // Inline bill-number generation (mirrors VendorBillRepository.GenerateNextBillNumberAsync;
            // SaveChanges per bill below keeps the sequence advancing).
            var last = await db.VendorBills.IgnoreQueryFilters()
                .OrderByDescending(b => b.Id)
                .Select(b => b.BillNumber)
                .FirstOrDefaultAsync();
            var billNumber = last != null && last.StartsWith("BILL-") && int.TryParse(last[5..], out var n)
                ? $"BILL-{n + 1:D5}"
                : "BILL-00001";

            var bill = new VendorBill
            {
                BillNumber = billNumber,
                VendorId = o.VendorId,
                ExpenseId = o.ExpenseId,
                CurrencyId = book.FunctionalCurrencyId,
                FxRate = 1m,
                Status = Forge.Core.Enums.VendorBillStatus.Approved,
                BillDate = billDate,
                DueDate = billDate, // due-on-receipt — the conservative default for reconstructed history
                Notes = $"Reconstructed from expense EXP-{o.ExpenseId} (legacy AP origination backfill).",
                Lines =
                {
                    new VendorBillLine
                    {
                        Description = expense is not null
                            ? $"Expense EXP-{expense.Id} — {expense.Category}: {expense.Description}"
                            : $"Expense EXP-{o.ExpenseId}",
                        Quantity = 1m,
                        UnitPrice = o.Amount,
                        LineNumber = 1,
                        AccountDeterminationKey = "OPERATING_EXPENSE",
                        JobId = expense?.JobId,
                    },
                },
            };

            db.VendorBills.Add(bill);
            await db.SaveChangesAsync(); // assign the id for the open item + advance the number sequence

            db.ApOpenItems.Add(new ApOpenItem
            {
                BookId = book.Id,
                VendorId = o.VendorId,
                SourceType = "VendorBill",
                SourceId = bill.Id,
                DocumentNumber = bill.BillNumber,
                DocumentDate = bill.BillDate,
                DueDate = bill.DueDate,
                CurrencyId = bill.CurrencyId,
                FxRate = 1m,
                OriginalTxnAmount = o.Amount,
                OriginalFunctionalAmount = o.Amount,
                Status = OpenItemStatus.Open,
            });
            created++;
        }

        await db.SaveChangesAsync();
        Log.Information(
            "Backfilled {Count} promoted vendor bills (+ open items) from legacy expense AP originations.",
            created);
    }

    internal static async Task EnsureCashInTransitAsync(AppDbContext db)
    {
        const string Key = "CASH_IN_TRANSIT";
        const string AccountNumber = "10150";

        var books = await db.Books.Where(b => b.IsActive).ToListAsync();
        if (books.Count == 0)
            return; // Fresh install — the run-once seed writes the full chart (incl. 10150) right after this returns.

        var bookIdsWithRule = await db.AccountDeterminationRules
            .Where(r => r.Key == Key)
            .Select(r => r.BookId)
            .ToListAsync();

        foreach (var book in books.Where(b => !bookIdsWithRule.Contains(b.Id)))
        {
            // Reuse a hand-inserted 10150 when one exists; otherwise create it
            // (exact construction style of the seeded chart above).
            var account = await db.GlAccounts
                .FirstOrDefaultAsync(a => a.BookId == book.Id && a.AccountNumber == AccountNumber);
            if (account is null)
            {
                account = new GlAccount
                {
                    BookId = book.Id,
                    AccountNumber = AccountNumber,
                    Name = "Cash in Transit",
                    AccountType = AccountType.Asset,
                    NormalBalance = NormalBalance.Debit,
                    IsControlAccount = false,
                    ControlType = null,
                    IsPostable = true,
                    IsActive = true,
                };
                db.GlAccounts.Add(account);
                await db.SaveChangesAsync(); // flush so account.Id is available for the rule
            }

            db.AccountDeterminationRules.Add(new AccountDeterminationRule
            {
                BookId = book.Id,
                Key = Key,
                GlAccountId = account.Id,
            });
            await db.SaveChangesAsync();

            Log.Information(
                "Backfilled GL account {Number} 'Cash in Transit' + {Key} determination rule for Book '{Book}' (additive chart patch for pre-seeded installs).",
                AccountNumber, Key, book.Code);
        }
    }
}
