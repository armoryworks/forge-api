using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Customer deposits billed on payment-milestone invoices: the deposit invoice posts Dr AR / Cr Customer Deposits,
/// the sales order's later invoices take the billed deposit off as a deposit line (Dr Customer Deposits, revenue at
/// the full amount), voids and credit memos give the deposit back, and AR control keeps tying to the open items.
/// </summary>
public class CustomerDepositPostingTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int SalesTaxPayableId = 104;
    private const int CustomerDepositsId = 109;
    private const int SalesReturnsId = 110;
    private const int FxGainId = 111;
    private const int FxLossId = 112;

    private static readonly DateTimeOffset InvoiceDate = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static InvoiceArPostingService ArPosting(AppDbContext db) => new(db, Engine(db), new FullGlOn());

    private static PaymentCashPostingService CashPosting(AppDbContext db)
        => new(db, Engine(db), new FullGlOn(), clock: new FixedClock(InvoiceDate));

    private static CreditMemoPostingService MemoPosting(AppDbContext db) => new(db, Engine(db), new FullGlOn());

    private static async Task<(AppDbContext Db, SalesOrder Order)> SeedAsync(int currencyId = UsdId)
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
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
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesReturnsId, BookId = BookId, AccountNumber = "41000", Name = "Sales Returns", AccountType = AccountType.Income, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "FX Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "FX Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = CustomerDepositsId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_RETURNS", GlAccountId = SalesReturnsId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });

        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-1001", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            TaxRate = 0m, CurrencyId = currencyId,
        };
        order.Lines.Add(new SalesOrderLine { Description = "Widget", Quantity = 10, UnitPrice = 100m, LineNumber = 1 });
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return (db, order);
    }

    private static async Task<Invoice> DraftAsync(
        AppDbContext db, SalesOrder order, string number, decimal unitPrice, decimal taxRate = 0m,
        decimal fxRate = 1m, PaymentSchedule? milestoneOf = null)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = number,
            CustomerId = order.CustomerId,
            SalesOrderId = order.Id,
            CurrencyId = order.CurrencyId ?? UsdId,
            FxRate = fxRate,
            InvoiceDate = InvoiceDate,
            DueDate = InvoiceDate,
            Status = InvoiceStatus.Draft,
            TaxRate = taxRate,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = unitPrice, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        if (milestoneOf is not null)
        {
            milestoneOf.Milestones.Add(new PaymentMilestone
            {
                Sequence = milestoneOf.Milestones.Count + 1, Name = "Deposit", Percentage = 50m,
                InvoiceId = invoice.Id, Status = PaymentMilestoneStatus.Invoiced,
            });
            await db.SaveChangesAsync();
        }

        return invoice;
    }

    private static async Task<PaymentSchedule> ScheduleAsync(AppDbContext db, SalesOrder order)
    {
        var schedule = new PaymentSchedule { SalesOrderId = order.Id, Status = PaymentScheduleStatus.Active };
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    private static Task SendAsync(AppDbContext db, Invoice invoice)
        => new SendInvoiceHandler(new InvoiceRepository(db), ArPosting(db), db: db)
            .Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

    private static Task VoidAsync(AppDbContext db, Invoice invoice)
        => new VoidInvoiceHandler(new InvoiceRepository(db), ArPosting(db), db: db, clock: new FixedClock(InvoiceDate))
            .Handle(new VoidInvoiceCommand(invoice.Id), CancellationToken.None);

    private static async Task<decimal> NetAsync(AppDbContext db, int accountId)
        => await (
                from line in db.JournalLines
                join entry in db.JournalEntries on line.JournalEntryId equals entry.Id
                where line.GlAccountId == accountId
                    && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                select line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount)
            .SumAsync();

    private static async Task AssertArTiesAsync(AppDbContext db)
    {
        var recon = await new ArAgingService(db, new FixedClock(InvoiceDate.AddDays(1)))
            .ReconcileAsync(BookId, new DateOnly(2026, 12, 31));
        recon.Difference.Should().Be(0m);
    }

    [Fact]
    public async Task DepositInvoice_CreditsCustomerDeposits_NotRevenue()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        var deposit = await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule);

        await SendAsync(db, deposit);

        (await NetAsync(db, CustomerDepositsId)).Should().Be(-500m);
        (await NetAsync(db, RevenueId)).Should().Be(0m);
        (await NetAsync(db, ArControlId)).Should().Be(500m);
        deposit.Lines.Should().ContainSingle().Which.LineType.Should().Be(DocumentLineType.Item);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task FinalInvoice_TakesTheDepositOff_AfterTax_AndRelievesCustomerDeposits()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        var deposit = await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule);
        await SendAsync(db, deposit);

        var final = await DraftAsync(db, order, "INV-F1", 1000m, taxRate: 0.08m);
        await SendAsync(db, final);

        var depositLine = final.Lines.Should().ContainSingle(l => l.LineType == DocumentLineType.Deposit).Subject;
        depositLine.UnitPrice.Should().Be(-500m);
        depositLine.IsTaxable.Should().BeFalse();
        depositLine.Description.Should().Contain("INV-D1");
        final.TaxAmount.Should().Be(80m);
        final.Total.Should().Be(580m);

        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        (await NetAsync(db, RevenueId)).Should().Be(-1000m);
        (await NetAsync(db, SalesTaxPayableId)).Should().Be(-80m);
        (await NetAsync(db, ArControlId)).Should().Be(1080m);
        (await db.ArOpenItems.SingleAsync(i => i.SourceId == final.Id)).OriginalTxnAmount.Should().Be(580m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task DepositLargerThanAnInvoice_CarriesTheRestToTheNextInvoice()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        await SendAsync(db, await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule));

        var first = await DraftAsync(db, order, "INV-F1", 300m);
        await SendAsync(db, first);
        var second = await DraftAsync(db, order, "INV-F2", 700m);
        await SendAsync(db, second);

        first.Total.Should().Be(0m);
        second.Total.Should().Be(500m);
        (await db.ArOpenItems.AnyAsync(i => i.SourceId == first.Id)).Should().BeFalse();
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        (await NetAsync(db, RevenueId)).Should().Be(-1000m);
        (await NetAsync(db, ArControlId)).Should().Be(1000m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task DraftAndVoidedDepositInvoices_AreNotTakenOff()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        await DraftAsync(db, order, "INV-D1", 200m, milestoneOf: schedule);
        var voided = await DraftAsync(db, order, "INV-D2", 300m, milestoneOf: schedule);
        await SendAsync(db, voided);
        await VoidAsync(db, voided);

        var final = await DraftAsync(db, order, "INV-F1", 1000m);
        await SendAsync(db, final);

        final.Lines.Should().NotContain(l => l.LineType == DocumentLineType.Deposit);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task VoidingTheFinalInvoice_GivesTheDepositBack_AndAnotherInvoiceUsesIt()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        await SendAsync(db, await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule));
        var final = await DraftAsync(db, order, "INV-F1", 1000m);
        await SendAsync(db, final);

        await VoidAsync(db, final);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(-500m);

        var reissued = await DraftAsync(db, order, "INV-F2", 1000m);
        await SendAsync(db, reissued);
        reissued.Total.Should().Be(500m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task VoidingADepositInvoiceAlreadyUsed_IsRefused()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        var deposit = await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule);
        await SendAsync(db, deposit);
        await SendAsync(db, await DraftAsync(db, order, "INV-F1", 1000m));

        var act = () => VoidAsync(db, deposit);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorCode == CustomerDeposits.DepositInUseCode);
        (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == deposit.Id)).Status.Should().Be(InvoiceStatus.Sent);
    }

    [Fact]
    public async Task PaidDeposit_ThenFinalInvoice_LeavesOnlyTheBalanceOwed()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        var deposit = await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule);
        await SendAsync(db, deposit);

        var payment = new Payment
        {
            PaymentNumber = "PMT-1", CustomerId = order.CustomerId, Method = PaymentMethod.Check, Amount = 500m,
            FxRate = 1m, PaymentDate = InvoiceDate,
            Applications = [new PaymentApplication { InvoiceId = deposit.Id, Amount = 500m, SettlementFxRate = 1m }],
        };
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();
        await CashPosting(db).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var final = await DraftAsync(db, order, "INV-F1", 1000m);
        await SendAsync(db, final);

        (await NetAsync(db, ArControlId)).Should().Be(500m);
        (await NetAsync(db, CashId)).Should().Be(500m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        (await NetAsync(db, RevenueId)).Should().Be(-1000m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task CreditMemoOnADepositInvoice_ReducesCustomerDeposits_AndTheDepositLaterTakenOff()
    {
        var (db, order) = await SeedAsync();
        var schedule = await ScheduleAsync(db, order);
        var deposit = await DraftAsync(db, order, "INV-D1", 500m, milestoneOf: schedule);
        await SendAsync(db, deposit);

        var memo = new CreditMemo
        {
            CreditMemoNumber = "CM-000001", CustomerId = order.CustomerId, CurrencyId = UsdId, FxRate = 1m,
            InvoiceId = deposit.Id, ReasonType = CreditMemoReasonType.PriceAdjustment, Reason = "Smaller deposit agreed",
            Status = CreditMemoStatus.Issued, CreditDate = InvoiceDate,
            Lines = [new CreditMemoLine { LineNumber = 1, LineType = CreditMemoLineType.Item, Description = "Deposit", Quantity = 1m, UnitPrice = 200m, TaxCode = InvoiceLine.NonTaxableCode }],
        };
        db.CreditMemos.Add(memo);
        await db.SaveChangesAsync();
        await MemoPosting(db).PostIssueAsync(memo.Id, issuedByUserId: 7);

        (await NetAsync(db, CustomerDepositsId)).Should().Be(-300m);
        (await NetAsync(db, SalesReturnsId)).Should().Be(0m);

        var final = await DraftAsync(db, order, "INV-F1", 1000m);
        await SendAsync(db, final);
        final.Total.Should().Be(700m);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        await AssertArTiesAsync(db);
    }

    [Fact]
    public async Task ForeignDeposit_IsRelievedAtTheRateItWasBooked()
    {
        var (db, order) = await SeedAsync(EurId);
        var schedule = await ScheduleAsync(db, order);
        await SendAsync(db, await DraftAsync(db, order, "INV-D1", 500m, fxRate: 1.10m, milestoneOf: schedule));
        (await NetAsync(db, CustomerDepositsId)).Should().Be(-550m);

        var final = await DraftAsync(db, order, "INV-F1", 1000m, fxRate: 1.20m);
        await SendAsync(db, final);

        (await NetAsync(db, CustomerDepositsId)).Should().Be(0m);
        (await NetAsync(db, RevenueId)).Should().Be(-1150m);
        (await NetAsync(db, ArControlId)).Should().Be(1150m);
        await AssertArTiesAsync(db);

        await VoidAsync(db, final);
        (await NetAsync(db, CustomerDepositsId)).Should().Be(-550m);
        (await NetAsync(db, RevenueId)).Should().Be(0m);
    }

    [Fact]
    public void Validator_RefusesACallerSuppliedDepositLine()
    {
        var command = new CreateInvoiceCommand(
            CustomerId: 1, SalesOrderId: 1, ShipmentId: null, InvoiceDate: InvoiceDate, DueDate: InvoiceDate,
            CreditTerms: null, TaxRate: 0m, Notes: null,
            Lines: [new CreateInvoiceLineModel(PartId: null, Description: "Less deposit", Quantity: 1m, UnitPrice: 0m, LineType: DocumentLineType.Deposit)]);

        new CreateInvoiceValidator().Validate(command).Errors
            .Should().Contain(e => e.ErrorCode == CustomerDeposits.ManualLineCode);
    }
}
