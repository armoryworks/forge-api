using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Payments;
using Forge.Api.Features.VendorPayments;
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

namespace Forge.Tests.Accounting.EarlyPay;

/// <summary>
/// Early-payment discounts end to end on the payment handlers and the cash postings: a 2/10 Net 30 invoice paid
/// within the window posts Dr Cash / Dr Sales Discounts / Cr AR and closes; paid late it stays partly open; a bill
/// paid early credits Purchase Discounts; the payment run suggests it; and a void restores every balance.
/// </summary>
public class EarlyPaymentDiscountPostingTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int ArControlId = 101;
    private const int SalesDiscountsId = 102;
    private const int DepositsId = 103;
    private const int ApControlId = 201;
    private const int PurchaseDiscountsId = 202;
    private const int AdvanceId = 203;

    private static readonly DateTimeOffset InvoiceDate = new(2026, 1, 25, 7, 0, 0, TimeSpan.Zero);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeCapabilities : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock(InvoiceDate.AddDays(8)));

    private static IHttpContextAccessor Http(string role = "OfficeManager")
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, role)], "test")),
            },
        };

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal, ControlAccountType? control = null)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = normal,
            IsControlAccount = control is not null, ControlType = control, IsPostable = true, IsActive = true,
        };

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/Denver", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            Account(CashId, "10100", "Cash", AccountType.Asset, NormalBalance.Debit),
            Account(ArControlId, "11000", "Accounts Receivable", AccountType.Asset, NormalBalance.Debit, ControlAccountType.AR),
            Account(SalesDiscountsId, "41500", "Sales Discounts", AccountType.Income, NormalBalance.Debit),
            Account(DepositsId, "24500", "Customer Deposits", AccountType.Liability, NormalBalance.Credit),
            Account(ApControlId, "20000", "Accounts Payable", AccountType.Liability, NormalBalance.Credit, ControlAccountType.AP),
            Account(PurchaseDiscountsId, "50500", "Purchase Discounts", AccountType.Expense, NormalBalance.Credit),
            Account(AdvanceId, "13500", "Prepaid Expense", AccountType.Asset, NormalBalance.Debit));
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_DISCOUNTS", GlAccountId = SalesDiscountsId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = DepositsId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_DISCOUNTS", GlAccountId = PurchaseDiscountsId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = AdvanceId });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Invoice> AddDiscountInvoiceAsync(AppDbContext db, decimal total = 9800m)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-2010", CustomerId = customer.Id, CurrencyId = UsdId, Status = InvoiceStatus.Sent,
            InvoiceDate = InvoiceDate, DueDate = InvoiceDate.AddDays(30),
            DiscountPercent = 2m, DiscountDueDate = InvoiceDate.AddDays(10),
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = total, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.ArOpenItems.Add(new ArOpenItem
        {
            BookId = BookId, CustomerId = customer.Id, SourceId = invoice.Id, DocumentNumber = invoice.InvoiceNumber,
            DocumentDate = invoice.InvoiceDate, DueDate = invoice.DueDate, CurrencyId = UsdId,
            OriginalTxnAmount = total, OriginalFunctionalAmount = total,
        });
        await db.SaveChangesAsync();
        return invoice;
    }

    private static async Task<VendorBill> AddDiscountBillAsync(AppDbContext db, decimal total = 5000m)
    {
        var vendor = new Vendor { CompanyName = "Steel Supply" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var bill = new VendorBill
        {
            BillNumber = "BILL-3001", VendorId = vendor.Id, CurrencyId = UsdId, Status = VendorBillStatus.Approved,
            BillDate = InvoiceDate, DueDate = InvoiceDate.AddDays(30),
            DiscountPercent = 2m, DiscountDueDate = InvoiceDate.AddDays(10),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = total, LineNumber = 1 }],
        };
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();
        db.ApOpenItems.Add(new ApOpenItem
        {
            BookId = BookId, VendorId = vendor.Id, SourceId = bill.Id, DocumentNumber = bill.BillNumber,
            DocumentDate = bill.BillDate, DueDate = bill.DueDate, CurrencyId = UsdId,
            OriginalTxnAmount = total, OriginalFunctionalAmount = total,
        });
        await db.SaveChangesAsync();
        return bill;
    }

    private static PaymentCashPostingService ArPosting(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(), clock: new FixedClock(InvoiceDate.AddDays(20)));

    private static VendorPaymentCashPostingService ApPosting(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(), clock: new FixedClock(InvoiceDate.AddDays(20)));

    private static CreatePaymentHandler PaymentHandler(AppDbContext db, string role = "OfficeManager")
        => new(new PaymentRepository(db), new CustomerRepository(db), new InvoiceRepository(db), db, ArPosting(db), Http(role));

    private static CreateVendorPaymentHandler VendorPaymentHandler(AppDbContext db)
        => new(new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db, ApPosting(db), Http());

    private static async Task<Dictionary<int, decimal>> NetByAccountAsync(AppDbContext db)
        => (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.GlAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

    private static async Task<Invoice> ReloadAsync(AppDbContext db, int id)
    {
        db.ChangeTracker.Clear();
        return await db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).SingleAsync(i => i.Id == id);
    }

    [Fact]
    public async Task Invoice_paid_on_day_8_takes_the_discount_and_closes()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);

        await PaymentHandler(db).Handle(new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9604m, InvoiceDate.AddDays(8), "CHK-1", null,
            [new CreatePaymentApplicationModel(invoice.Id, 9604m)]), CancellationToken.None);

        var net = await NetByAccountAsync(db);
        net[CashId].Should().Be(9604m);
        net[SalesDiscountsId].Should().Be(196m);
        net[ArControlId].Should().Be(-9800m);

        var reloaded = await ReloadAsync(db, invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Paid);
        reloaded.DiscountsTaken.Should().Be(196m);
        reloaded.BalanceDue.Should().Be(0m);
        var openItem = await db.ArOpenItems.SingleAsync();
        openItem.AppliedTxnAmount.Should().Be(9800m);
        openItem.Status.Should().Be(OpenItemStatus.Closed);
    }

    [Fact]
    public async Task Invoice_paid_on_day_12_takes_no_discount_and_stays_partly_paid()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);

        await PaymentHandler(db).Handle(new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9604m, InvoiceDate.AddDays(12), "CHK-1", null,
            [new CreatePaymentApplicationModel(invoice.Id, 9604m)]), CancellationToken.None);

        var net = await NetByAccountAsync(db);
        net.GetValueOrDefault(SalesDiscountsId).Should().Be(0m);
        net[ArControlId].Should().Be(-9604m);
        var reloaded = await ReloadAsync(db, invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        reloaded.BalanceDue.Should().Be(196m);
    }

    [Fact]
    public async Task Late_discount_is_refused_unless_a_manager_overrides()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);
        var command = new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9604m, InvoiceDate.AddDays(12), "CHK-1", null,
            [new CreatePaymentApplicationModel(invoice.Id, 9604m, DiscountAmount: 196m, AllowDiscountOverride: true)]);

        var refused = () => PaymentHandler(db, "OfficeManager").Handle(command, CancellationToken.None);
        (await refused.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == "discount-not-allowed");

        await PaymentHandler(db, "Manager").Handle(command, CancellationToken.None);

        (await ReloadAsync(db, invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);
        (await db.AuditLogEntries.CountAsync(a => a.Action == "PaymentDiscountOverride")).Should().Be(1);
    }

    [Fact]
    public async Task Cash_plus_discount_cannot_exceed_the_balance()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);

        var act = () => PaymentHandler(db).Handle(new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9700m, InvoiceDate.AddDays(8), "CHK-1", null,
            [new CreatePaymentApplicationModel(invoice.Id, 9700m, DiscountAmount: 196m)]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*plus discount*");
    }

    [Fact]
    public async Task Void_reverses_the_discount_and_reopens_the_invoice()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);
        var payment = await PaymentHandler(db).Handle(new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9604m, InvoiceDate.AddDays(8), "CHK-1", null,
            [new CreatePaymentApplicationModel(invoice.Id, 9604m)]), CancellationToken.None);

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(PaymentsSettings.ModificationPolicyKey, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        await new VoidPaymentHandler(new PaymentRepository(db), db, settings.Object, ArPosting(db), new FixedClock(InvoiceDate.AddDays(9)))
            .Handle(new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("Bounced")), CancellationToken.None);

        var net = await NetByAccountAsync(db);
        net[CashId].Should().Be(0m);
        net[SalesDiscountsId].Should().Be(0m);
        net[ArControlId].Should().Be(0m);
        var reloaded = await ReloadAsync(db, invoice.Id);
        reloaded.BalanceDue.Should().Be(9800m);
        reloaded.Status.Should().Be(InvoiceStatus.Sent);
        var openItem = await db.ArOpenItems.SingleAsync();
        openItem.AppliedTxnAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Applying_unapplied_cash_later_takes_the_discount_on_the_payment_date()
    {
        using var db = await SeedAsync();
        var invoice = await AddDiscountInvoiceAsync(db);
        var payment = await PaymentHandler(db).Handle(new CreatePaymentCommand(
            invoice.CustomerId, "Check", 9604m, InvoiceDate.AddDays(8), "CHK-1", null, null), CancellationToken.None);

        var settings = new Mock<ISettingsService>();
        await new ApplyPaymentHandler(new PaymentRepository(db), new InvoiceRepository(db), settings.Object, db, ArPosting(db), Http())
            .Handle(new ApplyPaymentCommand(payment.Id, [new CreatePaymentApplicationModel(invoice.Id, 9604m)]), CancellationToken.None);

        var net = await NetByAccountAsync(db);
        net[DepositsId].Should().Be(0m);
        net[SalesDiscountsId].Should().Be(196m);
        net[ArControlId].Should().Be(-9800m);
        (await ReloadAsync(db, invoice.Id)).Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task Bill_paid_on_day_8_credits_purchase_discounts_and_void_restores_it()
    {
        using var db = await SeedAsync();
        var bill = await AddDiscountBillAsync(db);

        var payment = await VendorPaymentHandler(db).Handle(new CreateVendorPaymentCommand(
            bill.VendorId, "Check", 4900m, InvoiceDate.AddDays(8), "CHK-9", null,
            [new CreateVendorPaymentApplicationModel(bill.Id, 4900m)]), CancellationToken.None);

        var net = await NetByAccountAsync(db);
        net[ApControlId].Should().Be(5000m);
        net[CashId].Should().Be(-4900m);
        net[PurchaseDiscountsId].Should().Be(-100m);
        db.ChangeTracker.Clear();
        var paid = await db.VendorBills.Include(b => b.Lines).Include(b => b.PaymentApplications).SingleAsync();
        paid.Status.Should().Be(VendorBillStatus.Paid);
        (await db.ApOpenItems.SingleAsync()).Status.Should().Be(OpenItemStatus.Closed);

        await new VoidVendorPaymentHandler(new VendorPaymentRepository(db), db, new FixedClock(InvoiceDate.AddDays(9)), ApPosting(db), Http())
            .Handle(new VoidVendorPaymentCommand(payment.Id, "Wrong bill"), CancellationToken.None);

        net = await NetByAccountAsync(db);
        net[ApControlId].Should().Be(0m);
        net[PurchaseDiscountsId].Should().Be(0m);
        db.ChangeTracker.Clear();
        var reopened = await db.VendorBills.Include(b => b.Lines).Include(b => b.PaymentApplications).SingleAsync();
        reopened.BalanceDue.Should().Be(5000m);
        reopened.Status.Should().Be(VendorBillStatus.Approved);
        (await db.ApOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Payment_run_suggests_the_bill_before_its_discount_deadline_only()
    {
        using var db = await SeedAsync();
        var bill = await AddDiscountBillAsync(db);
        var handler = new GetVendorPaymentSuggestionsHandler(db);

        var early = await handler.Handle(new GetVendorPaymentSuggestionsQuery(InvoiceDate.AddDays(8)), CancellationToken.None);
        var row = early.Should().ContainSingle().Subject;
        row.VendorBillId.Should().Be(bill.Id);
        row.SuggestedDiscount.Should().Be(100m);
        row.SuggestedPayAmount.Should().Be(4900m);
        row.DaysLeft.Should().Be(2);

        (await handler.Handle(new GetVendorPaymentSuggestionsQuery(InvoiceDate.AddDays(12)), CancellationToken.None))
            .Should().BeEmpty();

        var due = await handler.Handle(
            new GetVendorPaymentSuggestionsQuery(InvoiceDate.AddDays(12), InvoiceDate.AddDays(30)), CancellationToken.None);
        due.Should().ContainSingle().Which.SuggestedDiscount.Should().Be(0m);
    }

    [Fact]
    public async Task Paying_from_suggestions_creates_one_payment_per_vendor_through_the_normal_create()
    {
        using var db = await SeedAsync();
        var bill = await AddDiscountBillAsync(db);
        var sent = new List<CreateVendorPaymentCommand>();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateVendorPaymentCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<VendorPaymentListItemModel>, CancellationToken>((c, _) => sent.Add((CreateVendorPaymentCommand)c))
            .ReturnsAsync(new VendorPaymentListItemModel(1, "VP-1", bill.VendorId, "Steel Supply", "Check", 4900m, 4900m, 0m,
                InvoiceDate.AddDays(8), null, InvoiceDate));

        var result = await new CreateVendorPaymentsFromSuggestionsHandler(db, sender.Object).Handle(
            new CreateVendorPaymentsFromSuggestionsCommand(new CreateVendorPaymentsFromSuggestionsRequestModel(
                InvoiceDate.AddDays(8), "Check", "RUN-1", null, [new VendorPaymentSuggestionSelectionModel(bill.Id, 4900m, 100m)])),
            CancellationToken.None);

        result.Should().ContainSingle();
        var command = sent.Should().ContainSingle().Subject;
        command.Amount.Should().Be(4900m);
        command.Applications!.Should().ContainSingle(a => a.VendorBillId == bill.Id && a.DiscountAmount == 100m);
    }
}
