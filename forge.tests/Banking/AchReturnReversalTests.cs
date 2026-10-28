using System.Security.Claims;
using System.Text;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Banking;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Banking;

/// <summary>
/// ACH return of a transmitted vendor payment (graded run 3, S16-21): the return reverses the payment through the
/// void unwind. The bill reopens with its balance and any early-pay discount restored, the disbursement and the
/// bank-settlement journals reverse at their booked rates on the return date (Dr cash / Cr AP net), the payment is
/// voided with the return code on its audit trail, and a duplicate return file changes nothing.
/// </summary>
public class AchReturnReversalTests
{
    private const int UserId = 7;
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;

    private const int CashId = 100;
    private const int CashInTransitId = 101;
    private const int ApControlId = 201;
    private const int PurchaseDiscountsId = 202;
    private const int AdvanceId = 203;
    private const int FxGainId = 301;
    private const int FxLossId = 302;

    private static readonly DateTimeOffset BillDate = new(2026, 10, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReturnDate = new(2026, 10, 20);

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
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock(Now));

    private static VendorPaymentCashPostingService ApPosting(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(), clock: new FixedClock(Now));

    private static IHttpContextAccessor Http()
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, "OfficeManager")], "test")),
            },
        };

    private static BankReturnsService Returns(AppDbContext db)
        => new(db, new Mock<IMediator>().Object, new VendorPaymentRepository(db), new FixedClock(Now), ApPosting(db));

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal, ControlAccountType? control = null)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = normal,
            IsControlAccount = control is not null, ControlType = control, IsPostable = true, IsActive = true,
        };

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = UserId;
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
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
            Account(CashInTransitId, "10150", "Cash in Transit", AccountType.Asset, NormalBalance.Debit),
            Account(ApControlId, "20000", "Accounts Payable", AccountType.Liability, NormalBalance.Credit, ControlAccountType.AP),
            Account(PurchaseDiscountsId, "50500", "Purchase Discounts", AccountType.Expense, NormalBalance.Credit),
            Account(AdvanceId, "13500", "Prepaid Expense", AccountType.Asset, NormalBalance.Debit),
            Account(FxGainId, "90000", "Foreign Exchange Gain", AccountType.Income, NormalBalance.Credit),
            Account(FxLossId, "90100", "Foreign Exchange Loss", AccountType.Expense, NormalBalance.Debit));
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH_IN_TRANSIT", GlAccountId = CashInTransitId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_DISCOUNTS", GlAccountId = PurchaseDiscountsId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = AdvanceId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<VendorBill> AddBillAsync(
        AppDbContext db, decimal total, int currencyId = UsdId, decimal fxRate = 1m, decimal discountPercent = 0m)
    {
        var vendor = new Vendor { CompanyName = "Harbor Steel" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var bill = new VendorBill
        {
            BillNumber = $"BILL-{vendor.Id:D4}", VendorId = vendor.Id, CurrencyId = currencyId, FxRate = fxRate,
            Status = VendorBillStatus.Approved, BillDate = BillDate, DueDate = BillDate.AddDays(30),
            DiscountPercent = discountPercent, DiscountDueDate = discountPercent > 0m ? BillDate.AddDays(10) : null,
            Lines = [new VendorBillLine { Description = "Bar stock", Quantity = 1m, UnitPrice = total, LineNumber = 1 }],
        };
        db.VendorBills.Add(bill);
        await db.SaveChangesAsync();
        db.ApOpenItems.Add(new ApOpenItem
        {
            BookId = BookId, VendorId = vendor.Id, SourceId = bill.Id, DocumentNumber = bill.BillNumber,
            DocumentDate = bill.BillDate, DueDate = bill.DueDate, CurrencyId = currencyId,
            OriginalTxnAmount = total, OriginalFunctionalAmount = Math.Round(total * fxRate, 2),
        });
        await db.SaveChangesAsync();
        return bill;
    }

    /// <summary>Pays the bill by ACH, settles the transmission at the bank and puts it in a released batch under the trace.</summary>
    private static async Task<VendorPaymentListItemModel> PayAndSettleAsync(
        AppDbContext db, VendorBill bill, decimal amount, string trace,
        int? currencyId = null, decimal? fxRate = null, decimal? settlementFxRate = null)
    {
        var payment = await new CreateVendorPaymentHandler(
                new VendorPaymentRepository(db), new VendorRepository(db), new VendorBillRepository(db), db, ApPosting(db), Http())
            .Handle(new CreateVendorPaymentCommand(
                bill.VendorId, "BankTransfer", amount, BillDate.AddDays(8), null, null,
                [new CreateVendorPaymentApplicationModel(bill.Id, amount, settlementFxRate)],
                CurrencyId: currencyId, FxRate: fxRate), CancellationToken.None);

        var transmission = await db.PaymentTransmissions.SingleAsync(t => t.SourceId == payment.Id);
        transmission.Status = PaymentTransmissionStatus.Succeeded;
        await db.SaveChangesAsync();
        await new TransmissionSettlementService(db, new FixedClock(BillDate.AddDays(9)), Engine(db))
            .TryPostSettlementAsync(transmission, payment.PaymentNumber);

        var account = new VendorBankAccount
        {
            VendorId = bill.VendorId, Nickname = "Operating",
            RoutingNumberEncrypted = "enc", AccountNumberEncrypted = "enc",
            RoutingNumberMasked = "•0104", AccountNumberMasked = "•6789",
            Status = VendorBankAccountStatus.Verified, ChangedByUserId = 1,
        };
        var batch = new PaymentBatch
        {
            BatchNumber = $"ACH-{payment.Id:D5}", Status = PaymentBatchStatus.Released,
            EffectiveEntryDate = DateOnly.FromDateTime(BillDate.AddDays(9).UtcDateTime), CreatedByUserId = 1,
        };
        db.VendorBankAccounts.Add(account);
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();
        db.PaymentBatchItems.Add(new PaymentBatchItem
        {
            PaymentBatchId = batch.Id, VendorPaymentId = payment.Id, VendorBankAccountId = account.Id,
            Amount = amount, TraceNumber = trace,
        });
        await db.SaveChangesAsync();
        return payment;
    }

    /// <summary>A NACHA return file: a batch header carrying the return date, then one entry detail + addenda 99.</summary>
    private static string ReturnFile(string trace, string code, decimal amount, DateOnly? returnDate = null)
    {
        var sb = new StringBuilder();
        if (returnDate is DateOnly date)
            sb.Append(("5220FORGE".PadRight(69) + date.ToString("yyMMdd")).PadRight(94)).Append('\n');
        var entry = new StringBuilder(94)
            .Append('6').Append("26").Append("061000104")
            .Append("000123456789".PadRight(17))
            .Append(((long)(amount * 100)).ToString("D10"))
            .Append("RETURN".PadRight(15))
            .Append("VENDOR".PadRight(22))
            .Append("  ").Append('1')
            .Append("06100010").Append("0000001");
        sb.Append(entry).Append('\n');
        sb.Append(("799" + code.PadRight(3)[..3] + trace.PadRight(15)[..15]).PadRight(94)[..94]).Append('\n');
        return sb.ToString();
    }

    private static async Task<Dictionary<int, decimal>> NetByAccountAsync(AppDbContext db)
        => (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.GlAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

    private static async Task<VendorBill> ReloadBillAsync(AppDbContext db, int id)
    {
        db.ChangeTracker.Clear();
        return await db.VendorBills.Include(b => b.Lines).Include(b => b.PaymentApplications).Include(b => b.CreditApplications)
            .SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task Return_after_settlement_reopens_the_bill_and_reverses_cash_and_ap_on_the_return_date()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 420.50m);
        var payment = await PayAndSettleAsync(db, bill, 420.50m, "061000220000002");
        (await ReloadBillAsync(db, bill.Id)).Status.Should().Be(VendorBillStatus.Paid);
        (await NetByAccountAsync(db))[CashId].Should().Be(-420.50m);

        var result = await Returns(db).ApplyAsync(ReturnFile("061000220000002", "R01", 420.50m, ReturnDate), actorUserId: null);

        result.PaymentsReturned.Should().Be(1);
        var reopened = await ReloadBillAsync(db, bill.Id);
        reopened.Status.Should().Be(VendorBillStatus.Approved);
        reopened.PaymentApplications.Should().BeEmpty();
        reopened.BalanceDue.Should().Be(420.50m);

        var net = await NetByAccountAsync(db);
        net[CashId].Should().Be(0m);
        net[CashInTransitId].Should().Be(0m);
        net[ApControlId].Should().Be(0m);

        var entries = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync();
        entries.Should().HaveCount(4);
        entries.Where(e => e.Status == JournalEntryStatus.Reversed).Should().HaveCount(2);
        var reversalIds = entries.Where(e => e.ReversedByEntryId != null).Select(e => e.ReversedByEntryId!.Value).ToHashSet();
        var reversals = entries.Where(e => reversalIds.Contains(e.Id)).ToList();
        reversals.Should().HaveCount(2).And.OnlyContain(e => e.EntryDate == ReturnDate);
        reversals.SelectMany(e => e.Lines).Where(l => l.GlAccountId == CashId).Sum(l => l.Debit - l.Credit).Should().Be(420.50m);
        reversals.SelectMany(e => e.Lines).Where(l => l.GlAccountId == ApControlId).Sum(l => l.Debit - l.Credit).Should().Be(-420.50m);

        var openItem = await db.ApOpenItems.SingleAsync();
        openItem.AppliedTxnAmount.Should().Be(0m);
        openItem.Status.Should().Be(OpenItemStatus.Open);

        var voided = await db.VendorPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == payment.Id);
        voided.DeletedAt.Should().NotBeNull();
        var transmission = await db.PaymentTransmissions.SingleAsync(t => t.SourceId == payment.Id);
        transmission.Status.Should().Be(PaymentTransmissionStatus.Cancelled);
        transmission.LastError.Should().Contain("R01").And.Contain("Insufficient funds");
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorPayment" && a.EntityId == payment.Id && a.Action == "voided"
            && a.Description.Contains("R01") && a.Description.Contains("061000220000002"));
        db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "VendorBill" && a.EntityId == bill.Id && a.Action == "payment-voided" && a.Description.Contains("R01"));
    }

    [Fact]
    public async Task Duplicate_return_file_changes_nothing()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 300m);
        await PayAndSettleAsync(db, bill, 300m, "061000220000003");
        var file = ReturnFile("061000220000003", "R01", 300m, ReturnDate);

        await Returns(db).ApplyAsync(file, actorUserId: 1);
        var entriesAfterFirst = await db.JournalEntries.IgnoreQueryFilters().CountAsync();
        var second = await Returns(db).ApplyAsync(file, actorUserId: 1);

        second.PaymentsReturned.Should().Be(0);
        second.AlreadyApplied.Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(entriesAfterFirst);
        (await ReloadBillAsync(db, bill.Id)).BalanceDue.Should().Be(300m);
        (await db.ApOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
        (await NetByAccountAsync(db))[CashId].Should().Be(0m);
    }

    [Fact]
    public async Task Return_restores_the_early_pay_discount_taken()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 5000m, discountPercent: 2m);
        await PayAndSettleAsync(db, bill, 4900m, "061000220000004");
        (await NetByAccountAsync(db))[PurchaseDiscountsId].Should().Be(-100m);

        await Returns(db).ApplyAsync(ReturnFile("061000220000004", "R01", 4900m, ReturnDate), actorUserId: 1);

        var net = await NetByAccountAsync(db);
        net[PurchaseDiscountsId].Should().Be(0m);
        net[ApControlId].Should().Be(0m);
        net[CashId].Should().Be(0m);
        var reopened = await ReloadBillAsync(db, bill.Id);
        reopened.BalanceDue.Should().Be(5000m);
        reopened.Status.Should().Be(VendorBillStatus.Approved);
        (await db.ApOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Foreign_currency_return_reverses_at_the_booked_rates_including_the_fx_plug()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 1000m, currencyId: EurId, fxRate: 1.10m);
        await PayAndSettleAsync(db, bill, 1000m, "061000220000005", currencyId: EurId, fxRate: 1.05m, settlementFxRate: 1.05m);
        var paid = await NetByAccountAsync(db);
        paid[ApControlId].Should().Be(1100m);
        paid[CashId].Should().Be(-1050m);
        paid[FxGainId].Should().Be(-50m);

        await Returns(db).ApplyAsync(ReturnFile("061000220000005", "R02", 1050m, ReturnDate), actorUserId: 1);

        var net = await NetByAccountAsync(db);
        net[ApControlId].Should().Be(0m);
        net[CashId].Should().Be(0m);
        net[CashInTransitId].Should().Be(0m);
        net[FxGainId].Should().Be(0m);
        var item = await db.ApOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(0m);
        item.AppliedFunctionalAmount.Should().Be(0m);
        item.OpenFunctionalAmount.Should().Be(1100m);
        (await ReloadBillAsync(db, bill.Id)).BalanceDue.Should().Be(1000m);
    }

    [Fact]
    public async Task Return_without_a_dated_batch_header_reverses_on_the_processing_date()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 75m);
        await PayAndSettleAsync(db, bill, 75m, "061000220000006");

        await Returns(db).ApplyAsync(ReturnFile("061000220000006", "R03", 75m), actorUserId: 1);

        var entries = await db.JournalEntries.IgnoreQueryFilters().ToListAsync();
        var reversalIds = entries.Where(e => e.ReversedByEntryId != null).Select(e => e.ReversedByEntryId!.Value).ToHashSet();
        entries.Where(e => reversalIds.Contains(e.Id))
            .Should().HaveCount(2).And.OnlyContain(e => e.EntryDate == DateOnly.FromDateTime(Now.UtcDateTime));
    }

    [Fact]
    public async Task Return_for_a_trace_a_later_batch_superseded_is_skipped()
    {
        using var db = await SeedAsync();
        var bill = await AddBillAsync(db, 120m);
        var payment = await PayAndSettleAsync(db, bill, 120m, "061000220000007");
        var first = await db.PaymentBatchItems.SingleAsync(i => i.VendorPaymentId == payment.Id);
        db.PaymentBatchItems.Add(new PaymentBatchItem
        {
            PaymentBatchId = first.PaymentBatchId, VendorPaymentId = payment.Id, VendorBankAccountId = first.VendorBankAccountId,
            Amount = 120m, TraceNumber = "061000220000008",
        });
        await db.SaveChangesAsync();

        var result = await Returns(db).ApplyAsync(ReturnFile("061000220000007", "R01", 120m, ReturnDate), actorUserId: 1);

        result.PaymentsReturned.Should().Be(0);
        result.AlreadyApplied.Should().Be(1);
        (await ReloadBillAsync(db, bill.Id)).Status.Should().Be(VendorBillStatus.Paid);
        (await db.VendorPayments.AnyAsync(p => p.Id == payment.Id)).Should().BeTrue();
    }
}
