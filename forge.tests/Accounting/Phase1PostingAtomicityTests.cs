using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using System.Security.Claims;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Expenses;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// GO-LIVE BLOCKER verification (ACCOUNTING_SUITE_PLAN §2 "locked inline model"):
/// the Phase-1 posting handlers must commit the operational change and the inline
/// journal entry <b>atomically</b> — both, or neither. The fix wraps each handler's
/// operational SaveChanges and the engine's posting SaveChanges in a single
/// <c>BeginTransactionAsync</c>/<c>CommitAsync</c>; the engine's own SaveChanges
/// joins that transaction instead of self-committing.
/// <para>
/// These run against a real Postgres (Testcontainers) because the EF Core InMemory
/// provider <b>ignores transactions entirely</b> — a rollback there is a no-op, so it
/// cannot prove the guarantee. Each "rolls back" test forces the posting to fail
/// <i>after</i> the operational write (a deliberately-missing account-determination
/// rule → <see cref="PostingException"/> "DETERMINATION_UNMAPPED") and asserts, via a
/// fresh context, that the operational write did NOT survive and no journal entry
/// leaked. Each "commits" test proves the happy path still persists both.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Phase1PostingAtomicityTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    // Account ids — one chart spanning everything the three posting paths resolve.
    private const int CashId = 100;
    private const int SalesRevenueId = 101;
    private const int ArControlId = 102;
    private const int DeferredRevenueId = 103;
    private const int SalesTaxPayableId = 104;
    private const int CustomerDepositsId = 105;
    private const int ApControlId = 106;
    private const int OperatingExpenseId = 107;

    /// <summary>Toggleable capability snapshot provider for the FULLGL gate.</summary>
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    // ── Engine wired exactly like production (real Npgsql allocator → the row-locked
    // counter participates in the caller's transaction), minus the SoD authorizer
    // (null = the dark seam; we're testing atomicity, not segregation of duties).
    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    /// <summary>
    /// Resets the schema between tests. Uses TRUNCATE (not DELETE) because the
    /// ledger immutability triggers RAISE on DELETE of posted rows — TRUNCATE is not
    /// a row-level DELETE and bypasses them. CASCADE handles FK ordering across the
    /// whole public schema; the migration-history table is preserved.
    /// </summary>
    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    /// <summary>
    /// Seeds one book + an open FY/period + the full chart of accounts, and every
    /// account-determination rule EXCEPT the keys in <paramref name="omitRuleKeys"/>.
    /// Omitting a key is the lever that makes a posting fail at line-account
    /// resolution (after the operational write has already been flushed).
    /// </summary>
    private async Task SeedAccountingAsync(AppDbContext db, params string[] omitRuleKeys)
    {
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesRevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredRevenueId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        var rules = new (string Key, int AccountId)[]
        {
            ("CASH", CashId),
            ("AR_CONTROL", ArControlId),
            ("SALES_REVENUE", SalesRevenueId),
            ("DEFERRED_REVENUE", DeferredRevenueId),
            ("SALES_TAX_PAYABLE", SalesTaxPayableId),
            ("CUSTOMER_DEPOSITS", CustomerDepositsId),
            ("AP_CONTROL", ApControlId),
            ("OPERATING_EXPENSE", OperatingExpenseId),
        };
        foreach (var (key, accountId) in rules)
        {
            if (omitRuleKeys.Contains(key))
                continue;
            db.Set<AccountDeterminationRule>().Add(
                new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = accountId });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<int> AddCustomerAsync(AppDbContext db, string name = "Acme Corp")
    {
        var customer = new Customer { Name = name };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    // ─────────────────────────── CreatePayment ───────────────────────────

    [Fact]
    public async Task CreatePayment_postingFailure_rollsBackThePayment()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        // Omit CASH → the Dr CASH leg fails to resolve, after the payment is flushed.
        await SeedAccountingAsync(db, omitRuleKeys: "CASH");
        var customerId = await AddCustomerAsync(db);

        var handler = new CreatePaymentHandler(
            new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db,
            new PaymentCashPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)));

        var act = async () => await handler.Handle(
            new CreatePaymentCommand(customerId, "Check", 100m,
                new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), "REF-1", "on account", null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>())
            .Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        // Fresh context = committed state only. The payment must NOT have survived.
        await using var verify = fixture.CreateContext();
        (await verify.Payments.AnyAsync()).Should().BeFalse("the posting failure must roll the payment back");
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreatePayment_postingSucceeds_commitsPaymentAndJournalTogether()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAccountingAsync(db);
        var customerId = await AddCustomerAsync(db);

        var handler = new CreatePaymentHandler(
            new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db,
            new PaymentCashPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)));

        await handler.Handle(
            new CreatePaymentCommand(customerId, "Check", 100m,
                new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), "REF-1", "on account", null),
            CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Payments.CountAsync()).Should().Be(1);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AR);
        entry.SourceType.Should().Be("Payment");
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        // Dr Cash 100 / Cr Customer Deposits 100 (on-account, nothing applied).
        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == CustomerDepositsId).Credit.Should().Be(100m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    // ─────────────────────────── SendInvoice ───────────────────────────

    [Fact]
    public async Task SendInvoice_postingFailure_rollsBackTheStatusFlip()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        // Omit SALES_REVENUE → revenue leg fails to resolve after the Draft→Sent flip.
        await SeedAccountingAsync(db, omitRuleKeys: "SALES_REVENUE");
        var invoice = await AddDraftInvoiceAsync(db);

        var handler = new SendInvoiceHandler(
            new InvoiceRepository(db),
            new InvoiceArPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)),
            httpContextAccessor: null,
            db);

        var act = async () => await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>())
            .Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        await using var verify = fixture.CreateContext();
        // The invoice row still exists (it was pre-seeded) but the flip rolled back.
        (await verify.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status
            .Should().Be(InvoiceStatus.Draft, "the posting failure must roll the Draft→Sent flip back");
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SendInvoice_postingSucceeds_commitsStatusFlipAndJournalTogether()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAccountingAsync(db);
        var invoice = await AddDraftInvoiceAsync(db);

        var handler = new SendInvoiceHandler(
            new InvoiceRepository(db),
            new InvoiceArPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)),
            httpContextAccessor: null,
            db);

        await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Sent);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AR);
        entry.SourceType.Should().Be("Invoice");
        // No shipment + 0 tax → Dr AR 200 / Cr Sales Revenue 200.
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Debit.Should().Be(200m);
        entry.Lines.Where(l => l.GlAccountId == SalesRevenueId).Sum(l => l.Credit).Should().Be(200m);
    }

    private static async Task<Invoice> AddDraftInvoiceAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-1001",
            CustomerId = customer.Id,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Draft,
            TaxRate = 0m,
            Lines =
            [
                new InvoiceLine { Description = "Widget A", Quantity = 2, UnitPrice = 50m, LineNumber = 1 },
                new InvoiceLine { Description = "Widget B", Quantity = 1, UnitPrice = 100m, LineNumber = 2 },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    // ─────────────────────────── UpdateExpenseStatus ───────────────────────────

    [Fact]
    public async Task ApproveExpense_postingFailure_rollsBackTheApproval()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        // Omit OPERATING_EXPENSE → the Dr expense leg fails after the status update.
        await SeedAccountingAsync(db, omitRuleKeys: "OPERATING_EXPENSE");
        var expense = await AddPendingExpenseAsync(db);

        var handler = NewExpenseHandler(db);

        var act = async () => await handler.Handle(
            new UpdateExpenseStatusCommand(expense.Id,
                new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "looks good")),
            CancellationToken.None);

        (await act.Should().ThrowAsync<PostingException>())
            .Which.Code.Should().Be("DETERMINATION_UNMAPPED");

        await using var verify = fixture.CreateContext();
        (await verify.Expenses.SingleAsync(e => e.Id == expense.Id)).Status
            .Should().Be(ExpenseStatus.Pending, "the posting failure must roll the approval back");
        (await verify.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ApproveExpense_postingSucceeds_commitsApprovalAndJournalTogether()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAccountingAsync(db);
        var expense = await AddPendingExpenseAsync(db);

        var handler = NewExpenseHandler(db);

        await handler.Handle(
            new UpdateExpenseStatusCommand(expense.Id,
                new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "looks good")),
            CancellationToken.None);

        await using var verify = fixture.CreateContext();
        (await verify.Expenses.SingleAsync(e => e.Id == expense.Id)).Status.Should().Be(ExpenseStatus.Approved);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AP);
        entry.SourceType.Should().Be("Expense");
        // Cash-settled (no vendor) → Dr Operating Expense 75 / Cr Cash 75.
        entry.Lines.Single(l => l.GlAccountId == OperatingExpenseId).Debit.Should().Be(75m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(75m);
    }

    private static UpdateExpenseStatusHandler NewExpenseHandler(AppDbContext db)
        => new(
            new ExpenseRepository(db),
            HttpContextFor(7),
            new Mock<ISyncQueueRepository>().Object,
            new Mock<IAccountingProviderFactory>().Object,
            NullLogger<UpdateExpenseStatusHandler>.Instance,
            new ExpenseApPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: true)),
            billPromotion: null, // legacy-path coverage; promotion has its own suite
            db: db);

    private static async Task<Expense> AddPendingExpenseAsync(AppDbContext db)
    {
        var expense = new Expense
        {
            UserId = 42,
            Amount = 75m,
            Category = "Travel",
            Description = "Conference airfare",
            Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = ExpenseSettlementTarget.Cash,
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();
        return expense;
    }
}
