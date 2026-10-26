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
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Applying a payment's unapplied (on-account) cash to invoices after the payment was recorded:
/// the operational rules match payment creation, the GL moves the applied amount from CUSTOMER_DEPOSITS to
/// AR_CONTROL and relieves the AR open item, and a later void nets every account and open item back exactly once.
/// </summary>
public class ApplyPaymentLaterTests
{
    private const int UserId = 7;
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int ArControlId = 102;
    private const int CustomerDepositsId = 105;

    private static readonly DateTimeOffset Today = new(2026, 3, 2, 15, 0, 0, TimeSpan.Zero);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Today;
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PaymentCashPostingService PostingService(AppDbContext db, bool fullGlOn = true)
        => new(db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock()),
            new FakeCapabilities(fullGlOn),
            clock: new FixedClock());

    private static ISettingsService Policy(string? policy)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(PaymentsSettings.ModificationPolicyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        return settings.Object;
    }

    private static ApplyPaymentHandler ApplyHandler(
        AppDbContext db, IPaymentCashPostingService? posting = null, string? policy = null)
        => new(new PaymentRepository(db), new InvoiceRepository(db), Policy(policy), db, posting);

    private static VoidPaymentHandler VoidHandler(AppDbContext db, IPaymentCashPostingService posting)
        => new(new PaymentRepository(db), db, Policy(null), posting);

    private static async Task<AppDbContext> SeedBooksAsync()
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

    private static async Task<Customer> AddCustomerAsync(AppDbContext db, string name = "Acme Corp")
    {
        var customer = new Customer { Name = name };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<Invoice> AddInvoiceAsync(
        AppDbContext db, Customer customer, string number, decimal total,
        InvoiceStatus status = InvoiceStatus.Sent, bool withOpenItem = false)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = number,
            CustomerId = customer.Id,
            InvoiceDate = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero),
            Status = status,
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = total, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        if (withOpenItem)
        {
            db.ArOpenItems.Add(new ArOpenItem
            {
                BookId = BookId, CustomerId = customer.Id, SourceType = "Invoice", SourceId = invoice.Id,
                DocumentNumber = invoice.InvoiceNumber, DocumentDate = invoice.InvoiceDate, DueDate = invoice.DueDate,
                CurrencyId = UsdId, FxRate = 1m, OriginalTxnAmount = total, OriginalFunctionalAmount = total,
                Status = OpenItemStatus.Open,
            });
            await db.SaveChangesAsync();
        }

        return invoice;
    }

    private static async Task<Payment> AddPaymentAsync(
        AppDbContext db, Customer customer, decimal amount, params (Invoice invoice, decimal amount)[] applications)
    {
        var payment = new Payment
        {
            PaymentNumber = "PMT-9001",
            CustomerId = customer.Id,
            Method = PaymentMethod.Check,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Applications = applications
                .Select(a => new PaymentApplication { InvoiceId = a.invoice.Id, Amount = a.amount })
                .ToList(),
        };
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static ApplyPaymentCommand Apply(Payment payment, params (Invoice invoice, decimal amount)[] apps)
        => new(payment.Id, apps.Select(a => new CreatePaymentApplicationModel(a.invoice.Id, a.amount)).ToList());

    private static async Task<decimal> NetAsync(AppDbContext db, int accountId)
        => (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == accountId)
            .Sum(l => l.Debit - l.Credit);

    [Fact]
    public async Task Apply_OverpaymentCredit_ToSecondInvoice_ReducesBalanceAndUnapplied()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var first = await AddInvoiceAsync(db, customer, "INV-1", 1000m, InvoiceStatus.Paid);
        var second = await AddInvoiceAsync(db, customer, "INV-2", 400m);
        var payment = await AddPaymentAsync(db, customer, 1250m, (first, 1000m));

        var result = await ApplyHandler(db).Handle(Apply(payment, (second, 250m)), CancellationToken.None);

        result.AppliedAmount.Should().Be(1250m);
        result.UnappliedAmount.Should().Be(0m);
        var reloaded = await db.Invoices.Include(i => i.PaymentApplications).Include(i => i.Lines)
            .SingleAsync(i => i.Id == second.Id);
        reloaded.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        reloaded.BalanceDue.Should().Be(150m);
    }

    [Fact]
    public async Task Apply_OnAccountPayment_InFull_MarksInvoicePaid_AndBumpsPaymentVersion()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 500m);
        var payment = await AddPaymentAsync(db, customer, 500m);
        var versionBefore = payment.Version;

        await ApplyHandler(db).Handle(Apply(payment, (invoice, 500m)), CancellationToken.None);

        (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);
        (await db.Set<Payment>().SingleAsync(p => p.Id == payment.Id)).Version.Should().BeGreaterThan(versionBefore);
    }

    [Fact]
    public async Task Apply_MoreThanUnapplied_IsRefused()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var first = await AddInvoiceAsync(db, customer, "INV-1", 1000m, InvoiceStatus.Paid);
        var second = await AddInvoiceAsync(db, customer, "INV-2", 400m);
        var payment = await AddPaymentAsync(db, customer, 1250m, (first, 1000m));

        var act = () => ApplyHandler(db).Handle(Apply(payment, (second, 300m)), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("unapplied");
        (await db.Set<PaymentApplication>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Apply_OtherCustomersInvoice_IsRefused()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var other = await AddCustomerAsync(db, "Other Co");
        var invoice = await AddInvoiceAsync(db, other, "INV-9", 100m);
        var payment = await AddPaymentAsync(db, customer, 100m);

        var act = () => ApplyHandler(db).Handle(Apply(payment, (invoice, 100m)), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("different customer");
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Voided)]
    [InlineData(InvoiceStatus.Paid)]
    public async Task Apply_ToInvoiceThatCannotBePaid_IsRefused(InvoiceStatus status)
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 100m, status);
        var payment = await AddPaymentAsync(db, customer, 100m);

        var act = () => ApplyHandler(db).Handle(Apply(payment, (invoice, 100m)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Apply_MoreThanInvoiceBalance_IsRefused()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 100m);
        var payment = await AddPaymentAsync(db, customer, 500m);

        var act = () => ApplyHandler(db).Handle(Apply(payment, (invoice, 150m)), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("exceeds invoice");
    }

    [Fact]
    public async Task Apply_WhenPolicyLocked_IsRefused()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 100m);
        var payment = await AddPaymentAsync(db, customer, 100m);

        var act = () => ApplyHandler(db, policy: PaymentsSettings.PolicyLocked)
            .Handle(Apply(payment, (invoice, 100m)), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("locked");
    }

    [Fact]
    public void Validator_RejectsEmptyAndDuplicateApplications()
    {
        var validator = new ApplyPaymentValidator();

        validator.Validate(new ApplyPaymentCommand(1, [])).IsValid.Should().BeFalse();
        validator.Validate(new ApplyPaymentCommand(1,
            [new CreatePaymentApplicationModel(5, 10m), new CreatePaymentApplicationModel(5, 10m)]))
            .IsValid.Should().BeFalse();
        validator.Validate(new ApplyPaymentCommand(1, [new CreatePaymentApplicationModel(5, 0m)]))
            .IsValid.Should().BeFalse();
        validator.Validate(new ApplyPaymentCommand(1, [new CreatePaymentApplicationModel(5, 10m)]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_FullGlOn_MovesDepositsToAr_AndRelievesOpenItem()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 400m, withOpenItem: true);
        var payment = await AddPaymentAsync(db, customer, 500m);
        var posting = PostingService(db);
        await posting.PostPaymentCreatedAsync(payment.Id, UserId);

        await ApplyHandler(db, posting).Handle(Apply(payment, (invoice, 250m)), CancellationToken.None);

        var applyEntry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey!.EndsWith(":APPLY"));
        applyEntry.SourceType.Should().Be("Payment");
        applyEntry.SourceId.Should().Be(payment.Id);
        applyEntry.EntryDate.Should().Be(DateOnly.FromDateTime(Today.UtcDateTime));
        applyEntry.Lines.Single(l => l.GlAccountId == CustomerDepositsId).Debit.Should().Be(250m);
        var arLine = applyEntry.Lines.Single(l => l.GlAccountId == ArControlId);
        arLine.Credit.Should().Be(250m);
        arLine.SubledgerPartyId.Should().Be(customer.Id);

        (await NetAsync(db, CashId)).Should().Be(500m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(-250m);
        (await NetAsync(db, ArControlId)).Should().Be(-250m);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(250m);
        item.AppliedFunctionalAmount.Should().Be(250m);
        item.OpenFunctionalAmount.Should().Be(150m);
    }

    [Fact]
    public async Task Apply_ForeignDeposit_RelievesDepositsAtThePaymentRate_WhateverTheSettlementRate()
    {
        using var db = await SeedBooksAsync();
        db.Set<Currency>().Add(new Currency { Id = 2, Code = "CAD", Name = "Canadian Dollar", Symbol = "$" });
        db.Set<GlAccount>().Add(new GlAccount { Id = 190, BookId = BookId, AccountNumber = "90000", Name = "FX Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = 190 });
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 400m, withOpenItem: true);
        invoice.CurrencyId = 2;
        invoice.FxRate = 0.73m;
        var item = await db.ArOpenItems.SingleAsync();
        item.CurrencyId = 2;
        item.FxRate = 0.73m;
        item.OriginalFunctionalAmount = 292m;
        var payment = await AddPaymentAsync(db, customer, 500m);
        payment.CurrencyId = 2;
        payment.FxRate = 0.75m;
        await db.SaveChangesAsync();
        var posting = PostingService(db);
        await posting.PostPaymentCreatedAsync(payment.Id, UserId);

        await ApplyHandler(db, posting).Handle(
            new ApplyPaymentCommand(payment.Id, [new CreatePaymentApplicationModel(invoice.Id, 400m, 0.80m)]),
            CancellationToken.None);

        (await NetAsync(db, CashId)).Should().Be(375m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(-75m);
        (await NetAsync(db, ArControlId)).Should().Be(-292m);
        (await NetAsync(db, 190)).Should().Be(-8m);
    }

    [Fact]
    public async Task PostApplications_IsIdempotent()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 400m, withOpenItem: true);
        var payment = await AddPaymentAsync(db, customer, 500m);
        var posting = PostingService(db);
        await posting.PostPaymentCreatedAsync(payment.Id, UserId);
        await ApplyHandler(db, posting).Handle(Apply(payment, (invoice, 250m)), CancellationToken.None);
        var appId = (await db.Set<PaymentApplication>().SingleAsync()).Id;

        await posting.PostPaymentApplicationsAsync(payment.Id, [appId], UserId);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await db.ArOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(250m);
    }

    [Fact]
    public async Task Apply_FullGlOff_PostsNothing()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 400m, withOpenItem: true);
        var payment = await AddPaymentAsync(db, customer, 500m);

        await ApplyHandler(db, PostingService(db, fullGlOn: false))
            .Handle(Apply(payment, (invoice, 250m)), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.ArOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Apply_FullGlOn_PaymentReceiptNeverPosted_PostsNothing()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var invoice = await AddInvoiceAsync(db, customer, "INV-1", 400m, withOpenItem: true);
        var payment = await AddPaymentAsync(db, customer, 500m);

        await ApplyHandler(db, PostingService(db)).Handle(Apply(payment, (invoice, 250m)), CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.ArOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Void_AfterLaterApplication_NetsEveryAccountAndOpenItemOnce()
    {
        using var db = await SeedBooksAsync();
        var customer = await AddCustomerAsync(db);
        var first = await AddInvoiceAsync(db, customer, "INV-1", 300m, withOpenItem: true);
        var second = await AddInvoiceAsync(db, customer, "INV-2", 400m, withOpenItem: true);
        var payment = await AddPaymentAsync(db, customer, 600m, (first, 300m));
        first.Status = InvoiceStatus.Paid;
        await db.SaveChangesAsync();
        var posting = PostingService(db);
        await posting.PostPaymentCreatedAsync(payment.Id, UserId);
        await ApplyHandler(db, posting).Handle(Apply(payment, (second, 200m)), CancellationToken.None);

        var otherPayment = new Payment
        {
            PaymentNumber = "PMT-9002", CustomerId = customer.Id, Method = PaymentMethod.Check, Amount = 100m,
            PaymentDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            Applications = [new PaymentApplication { InvoiceId = second.Id, Amount = 100m }],
        };
        db.Set<Payment>().Add(otherPayment);
        await db.SaveChangesAsync();
        await posting.PostPaymentCreatedAsync(otherPayment.Id, UserId);

        await VoidHandler(db, posting).Handle(
            new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("bounced check")), CancellationToken.None);

        (await NetAsync(db, CashId)).Should().Be(100m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        (await NetAsync(db, ArControlId)).Should().Be(-100m);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(5);

        var firstItem = await db.ArOpenItems.SingleAsync(i => i.SourceId == first.Id);
        firstItem.AppliedTxnAmount.Should().Be(0m);
        firstItem.Status.Should().Be(OpenItemStatus.Open);
        var secondItem = await db.ArOpenItems.SingleAsync(i => i.SourceId == second.Id);
        secondItem.AppliedTxnAmount.Should().Be(100m);
        secondItem.AppliedFunctionalAmount.Should().Be(100m);
        (await db.Invoices.SingleAsync(i => i.Id == second.Id)).Status.Should().Be(InvoiceStatus.PartiallyPaid);
    }
}
