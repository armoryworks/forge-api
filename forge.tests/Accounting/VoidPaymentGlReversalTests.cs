using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// AR parity fix — <c>VoidPayment</c> now reverses the payment's cash-receipt origination journal via
/// <see cref="PaymentCashPostingService.ReversePaymentCreatedAsync"/>: with FULLGL on, voiding nets the
/// CASH debit and the AR-control credit back to zero (previously the GL silently kept both for a payment
/// that no longer existed operationally). With the posting service absent (legacy construction) or FULLGL
/// off, the void keeps working unchanged — no throw, no journals.
/// </summary>
public class VoidPaymentGlReversalTests
{
    private const int UserId = 7;

    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int ArControlId = 102;
    private const int CustomerDepositsId = 105;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PaymentCashPostingService PostingService(AppDbContext db, bool fullGlOn)
        => new(db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
            new FakeCapabilities(fullGlOn));

    private static ISettingsService FullPolicySettings()
    {
        // GetStringAsync returns null → the handler falls back to PolicyFull (the default).
        return new Mock<ISettingsService>().Object;
    }

    private static VoidPaymentHandler Handler(AppDbContext db, IPaymentCashPostingService? cashPosting)
        => new(new PaymentRepository(db), db, FullPolicySettings(), cashPosting);

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = UserId;

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = CustomerDepositsId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Customer + Sent invoice (100) + payment (100) fully applied to it.</summary>
    private static async Task<(Payment payment, Invoice invoice)> AddPaidInvoiceAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-9001",
            CustomerId = customer.Id,
            InvoiceDate = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Sent,
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            PaymentNumber = "PMT-9001",
            CustomerId = customer.Id,
            Method = PaymentMethod.Check,
            Amount = 100m,
            PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Applications = [new PaymentApplication { InvoiceId = invoice.Id, Amount = 100m }],
        };
        db.Set<Payment>().Add(payment);
        invoice.Status = InvoiceStatus.Paid;
        await db.SaveChangesAsync();
        return (payment, invoice);
    }

    [Fact]
    public async Task Void_FullGlOn_ReversesCashReceipt_CashAndArNetToZero()
    {
        using var db = await SeedAsync();
        var (payment, invoice) = await AddPaidInvoiceAsync(db);

        var posting = PostingService(db, fullGlOn: true);
        await posting.PostPaymentCreatedAsync(payment.Id, UserId);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);

        await Handler(db, posting).Handle(
            new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("bounced check")), CancellationToken.None);

        // Origination + reversal; CASH and AR control both net to zero for the payment.
        var entries = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Status == JournalEntryStatus.Reversed);
        entries.SelectMany(e => e.Lines).Where(l => l.GlAccountId == CashId)
            .Sum(l => l.Debit - l.Credit).Should().Be(0m);
        entries.SelectMany(e => e.Lines).Where(l => l.GlAccountId == ArControlId)
            .Sum(l => l.Credit - l.Debit).Should().Be(0m);

        // Operational void behavior unchanged: payment soft-deleted, invoice reopened.
        (await db.Set<Payment>().AnyAsync(p => p.Id == payment.Id)).Should().BeFalse();
        (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Sent);
    }

    [Fact]
    public async Task Void_FullGlOn_CalledForPaymentWithoutOrigination_NoThrow_NoJournal()
    {
        // The payment was recorded while FULLGL was OFF (no origination entry). Voiding with FULLGL ON
        // must not invent a reversal of nothing.
        using var db = await SeedAsync();
        var (payment, invoice) = await AddPaidInvoiceAsync(db);

        await Handler(db, PostingService(db, fullGlOn: true)).Handle(
            new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("recorded in error")), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.Set<Payment>().AnyAsync(p => p.Id == payment.Id)).Should().BeFalse();
        (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Sent);
    }

    [Fact]
    public async Task Void_NullPostingService_StillWorks()
    {
        // Legacy construction (no posting seam) — the optional parameter keeps the old behavior intact.
        using var db = await SeedAsync();
        var (payment, invoice) = await AddPaidInvoiceAsync(db);

        await Handler(db, cashPosting: null).Handle(
            new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("dup")), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.Set<Payment>().AnyAsync(p => p.Id == payment.Id)).Should().BeFalse();
        (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Sent);
    }

    [Fact]
    public async Task Void_FullGlOff_NoReversal_StillWorks()
    {
        using var db = await SeedAsync();
        var (payment, invoice) = await AddPaidInvoiceAsync(db);

        await Handler(db, PostingService(db, fullGlOn: false)).Handle(
            new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("dup")), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.Set<Payment>().AnyAsync(p => p.Id == payment.Id)).Should().BeFalse();
        (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Sent);
    }
}
