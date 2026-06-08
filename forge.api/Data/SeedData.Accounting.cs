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

    private static async Task SeedAccountingAsync(AppDbContext db)
    {
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
}
