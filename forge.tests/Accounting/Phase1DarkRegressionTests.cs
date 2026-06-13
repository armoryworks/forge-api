using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

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
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-1 STAGE F — CRITICAL non-regression tests. The locked guardrail for the
/// whole Phase-1 increment is: with <c>CAP-ACCT-FULLGL</c> <b>OFF</b> (the default)
/// the invoice / payment / expense operational command handlers must behave
/// <b>exactly as they did before Phase-1</b> — i.e. the inline posting wiring is a
/// true no-op: <b>no JournalEntry is created, no exception is thrown, and the
/// operational outputs are identical</b> to running the handler with no posting
/// service wired at all.
///
/// <para>These tests wire the <b>real</b> posting services (not a stub) into the
/// real command handlers, backed by a <b>fully seeded accounting book</b> (book +
/// chart of accounts + determination rules + open period). Seeding the book is
/// deliberate: it proves the no-op is enforced by the <b>FULLGL capability gate</b>
/// — not merely by a missing book or empty schema. If the dark gate ever
/// regressed (FULLGL falsely treated as on), the engine <i>would</i> find
/// everything it needs and post — and these assertions would fail. While dark,
/// the ledger stays empty.</para>
///
/// <para>Companion to the Stage A–E service-level tests
/// (<see cref="InvoiceArPostingServiceTests"/>, <see cref="PaymentCashPostingServiceTests"/>,
/// <see cref="ExpenseApPostingServiceTests"/>), which prove the FULLGL-<b>ON</b>
/// posting behavior. This file proves the FULLGL-<b>OFF</b> command-level
/// non-regression that the existing invoice/payment/expense tests depend on.</para>
/// </summary>
public class Phase1DarkRegressionTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    // Account ids (a small but complete CoA so the engine could post if ungated).
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int DeferredRevenueId = 103;
    private const int SalesTaxPayableId = 104;
    private const int CustomerDepositsId = 105;
    private const int ApControlId = 106;
    private const int OperatingExpenseId = 107;

    /// <summary>In-process allocator (InMemory can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    /// <summary>Capability snapshot provider pinned to the dark default (FULLGL OFF).</summary>
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A shared InMemory accounting context seeded with a complete posting book.
    /// The same instance is used both by the posting service and for ledger
    /// assertions, so "no JournalEntry created" is asserted against the very
    /// context the service was handed.
    /// </summary>
    private static async Task<AppDbContext> SeedAccountingAsync()
    {
        var db = TestDbContextFactory.Create();

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
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = DeferredRevenueId, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredRevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = CustomerDepositsId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId });

        await db.SaveChangesAsync();
        return db;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    /// <summary>Asserts the ledger (and the balance store) is completely untouched.</summary>
    private static async Task AssertNoLedgerMovementAsync(AppDbContext db)
    {
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse(
            "with CAP-ACCT-FULLGL OFF no JournalEntry may be created by an operational command");
        (await db.Set<JournalLine>().IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.LedgerBalances.AnyAsync()).Should().BeFalse();
    }

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        return accessor.Object;
    }

    // ── Invoice finalize (SendInvoiceHandler + InvoiceArPostingService) ──────────

    [Fact]
    public async Task SendInvoice_WithFullGlOff_FinalizesNormally_AndPostsNothing()
    {
        using var db = await SeedAccountingAsync();

        // An operational invoice the AR posting service would otherwise read.
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-2001",
            CustomerId = customer.Id,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Draft,
            TaxRate = 0.08m,
            Lines =
            [
                new InvoiceLine { Description = "Widget A", Quantity = 2, UnitPrice = 50m, LineNumber = 1 },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(r => r.FindAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // The REAL posting service, dark (FULLGL OFF), on the seeded book.
        var arPosting = new InvoiceArPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new SendInvoiceHandler(repo.Object, arPosting, HttpContextFor(7));

        // Act — must complete without throwing.
        await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        // Operational behavior unchanged: status flips Draft → Sent and the
        // operational SaveChanges still runs exactly once.
        invoice.Status.Should().Be(InvoiceStatus.Sent);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Dark: zero ledger movement.
        await AssertNoLedgerMovementAsync(db);
    }

    [Fact]
    public async Task SendInvoice_WithFullGlOff_NonDraft_StillRejects_NoPosting()
    {
        // The pre-existing guard (only Draft invoices can be sent) must be
        // preserved verbatim, and no posting may sneak in on the rejected path.
        using var db = await SeedAccountingAsync();

        var invoice = new Invoice { InvoiceNumber = "INV-2002", Status = InvoiceStatus.Sent };
        var repo = new Mock<IInvoiceRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var arPosting = new InvoiceArPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new SendInvoiceHandler(repo.Object, arPosting, HttpContextFor(7));

        var act = () => handler.Handle(new SendInvoiceCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only Draft invoices*");
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await AssertNoLedgerMovementAsync(db);
    }

    // ── Payment create (CreatePaymentHandler + PaymentCashPostingService) ────────

    [Fact]
    public async Task CreatePayment_WithFullGlOff_CreatesPaymentNormally_AndPostsNothing()
    {
        using var db = await SeedAccountingAsync();

        var customer = new Customer { Id = 555, Name = "Acme Corp" };
        var paymentRepo = new Mock<IPaymentRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var invoiceRepo = new Mock<IInvoiceRepository>();

        customerRepo.Setup(r => r.FindAsync(555, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        paymentRepo.Setup(r => r.GenerateNextPaymentNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PMT-2001");

        Payment? captured = null;
        paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) =>
            {
                // Assign an Id as the real repo/EF would on add, so the (no-op)
                // posting service has a persisted-looking payment to reference.
                p.Id = 9100;
                captured = p;
            })
            .Returns(Task.CompletedTask);
        paymentRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var cashPosting = new PaymentCashPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new CreatePaymentHandler(
            paymentRepo.Object, customerRepo.Object, invoiceRepo.Object, db,
            cashPosting, HttpContextFor(7));

        var command = new CreatePaymentCommand(
            555, "Check", 100m, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            "REF-1", "note", null);

        // Act — must complete without throwing and return the same shaped result.
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.PaymentNumber.Should().Be("PMT-2001");
        result.CustomerId.Should().Be(555);
        result.Amount.Should().Be(100m);
        result.AppliedAmount.Should().Be(0m);
        result.UnappliedAmount.Should().Be(100m);

        captured.Should().NotBeNull();
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
        paymentRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Dark: zero ledger movement.
        await AssertNoLedgerMovementAsync(db);
    }

    [Fact]
    public async Task CreatePayment_WithFullGlOff_CustomerNotFound_StillThrows_NoPosting()
    {
        // Pre-existing not-found guard preserved; no posting on the failed path.
        using var db = await SeedAccountingAsync();

        var paymentRepo = new Mock<IPaymentRepository>();
        var customerRepo = new Mock<ICustomerRepository>();
        var invoiceRepo = new Mock<IInvoiceRepository>();
        customerRepo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var cashPosting = new PaymentCashPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new CreatePaymentHandler(
            paymentRepo.Object, customerRepo.Object, invoiceRepo.Object, db,
            cashPosting, HttpContextFor(7));

        var command = new CreatePaymentCommand(
            42, "Check", 100m, DateTimeOffset.UtcNow, null, null, null);

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Customer 42*");
        paymentRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await AssertNoLedgerMovementAsync(db);
    }

    // ── Expense approve (UpdateExpenseStatusHandler + ExpenseApPostingService) ───

    [Fact]
    public async Task UpdateExpenseStatus_Approve_WithFullGlOff_ApprovesNormally_AndPostsNothing()
    {
        using var db = await SeedAccountingAsync();

        var vendor = new Vendor { Id = 3001, CompanyName = "Delta", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        var expense = new Expense
        {
            Id = 7000, UserId = 42, Amount = 250m, Category = "Travel",
            Description = "Airfare", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = ExpenseSettlementTarget.AccountsPayable, VendorId = 3001,
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();

        var repo = new Mock<IExpenseRepository>();
        repo.Setup(r => r.FindAsync(7000, It.IsAny<CancellationToken>())).ReturnsAsync(expense);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var responseModel = new ExpenseResponseModel(
            7000, 42, "Jane", null, null, 250m, "Travel", "Airfare", null,
            ExpenseStatus.Approved, 7, "Jane", null,
            expense.ExpenseDate, DateTimeOffset.UtcNow);
        repo.Setup(r => r.GetByIdAsync(7000, It.IsAny<CancellationToken>())).ReturnsAsync(responseModel);

        // The QB-sync side path: no active provider so it short-circuits cleanly.
        var providerFactory = new Mock<IAccountingProviderFactory>();
        providerFactory.Setup(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAccountingService?)null);
        var syncQueue = new Mock<ISyncQueueRepository>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<UpdateExpenseStatusHandler>>();

        var apPosting = new ExpenseApPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new UpdateExpenseStatusHandler(
            repo.Object, HttpContextFor(7), syncQueue.Object, providerFactory.Object,
            logger.Object, apPosting);

        var command = new UpdateExpenseStatusCommand(
            7000, new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "ok"));

        // Act — approving must complete without throwing and return the model.
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be(ExpenseStatus.Approved);
        // Operational mutation preserved: status + approver applied, SaveChanges once.
        expense.Status.Should().Be(ExpenseStatus.Approved);
        expense.ApprovedBy.Should().Be(7);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Dark: zero ledger movement even on the approve transition.
        await AssertNoLedgerMovementAsync(db);
    }

    [Fact]
    public async Task UpdateExpenseStatus_NonApprove_WithFullGlOff_PostsNothing()
    {
        // A non-approving transition never reaches the posting branch even when
        // FULLGL is on; with it off the whole flow is unchanged. Asserts the
        // branch guard (Approved/SelfApproved) is intact alongside the dark gate.
        using var db = await SeedAccountingAsync();

        var expense = new Expense
        {
            Id = 7001, UserId = 42, Amount = 99m, Category = "Meals",
            Description = "Dinner", Status = ExpenseStatus.Pending,
            ExpenseDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };
        db.Set<Expense>().Add(expense);
        await db.SaveChangesAsync();

        var repo = new Mock<IExpenseRepository>();
        repo.Setup(r => r.FindAsync(7001, It.IsAny<CancellationToken>())).ReturnsAsync(expense);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(7001, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ExpenseResponseModel(
                7001, 42, "Jane", null, null, 99m, "Meals", "Dinner", null,
                ExpenseStatus.Rejected, 7, "Jane", "not allowed per policy ok",
                expense.ExpenseDate, DateTimeOffset.UtcNow));

        var providerFactory = new Mock<IAccountingProviderFactory>();
        var syncQueue = new Mock<ISyncQueueRepository>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<UpdateExpenseStatusHandler>>();

        var apPosting = new ExpenseApPostingService(db, Engine(db), new FakeCapabilities(fullGlOn: false));
        var handler = new UpdateExpenseStatusHandler(
            repo.Object, HttpContextFor(7), syncQueue.Object, providerFactory.Object,
            logger.Object, apPosting);

        var command = new UpdateExpenseStatusCommand(
            7001, new UpdateExpenseStatusRequestModel(ExpenseStatus.Rejected, "not allowed per policy ok"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be(ExpenseStatus.Rejected);
        expense.Status.Should().Be(ExpenseStatus.Rejected);
        // A rejection must not even consult the QB sync path.
        providerFactory.Verify(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()), Times.Never);
        await AssertNoLedgerMovementAsync(db);
    }
}
