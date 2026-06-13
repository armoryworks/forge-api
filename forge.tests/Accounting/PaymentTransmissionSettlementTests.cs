using FluentAssertions;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §7 BANK-002 cash-in-transit SETTLEMENT — the <see cref="PaymentTransmissionJob"/> success path posts
/// Dr CASH_IN_TRANSIT / Cr CASH for the exact in-transit amount the payment's origination credited, so the
/// payment's net CIT balance returns to zero once the bank accepts the submission. Proves the gates
/// (no origination → skip; legacy non-CIT origination → skip), engine-keyed idempotency on job re-runs,
/// the realized-FX amount (settle the FUNCTIONAL in-transit amount, not the AP relief), and that a
/// settlement-posting failure never un-succeeds the transmission.
/// </summary>
public class PaymentTransmissionSettlementTests
{
    private const int CreatorUserId = 7;

    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ApControlId = 200;
    private const int CashId = 202;
    private const int PrepaidExpenseId = 203;
    private const int FxGainId = 212;
    private const int FxLossId = 213;
    private const int CashInTransitId = 214;

    private static readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

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

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock(Now));

    private static VendorPaymentCashPostingService OriginationService(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn: true));

    private static PaymentTransmissionJob BuildJob(AppDbContext db, Mock<IBankPaymentService> bank)
        => new(db, bank.Object, new Mock<IBackgroundJobClient>().Object, new Mock<IMediator>().Object,
            new FixedClock(Now), NullLogger<PaymentTransmissionJob>.Instance, Engine(db));

    private static Mock<IBankPaymentService> BankSucceeding()
    {
        var bank = new Mock<IBankPaymentService>();
        bank.Setup(b => b.SubmitPaymentAsync(It.IsAny<BankPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankSubmissionResult(true, "MOCK-ACH-1", null));
        return bank;
    }

    private static async Task<(AppDbContext db, int vendorId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        // One period spanning the year so both the payment date and the (clock-driven) settlement date post.
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PrepaidExpenseId, BookId = BookId, AccountNumber = "12000", Name = "Prepaid Expenses", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashInTransitId, BookId = BookId, AccountNumber = "10150", Name = "Cash in Transit", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = PrepaidExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH_IN_TRANSIT", GlAccountId = CashInTransitId });

        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (db, vendor.Id);
    }

    private static async Task<VendorPayment> AddPaymentAsync(
        AppDbContext db, int vendorId, decimal amount,
        PaymentMethod method = PaymentMethod.BankTransfer,
        int? billId = null, decimal? appliedAmount = null, decimal settlementFxRate = 1m)
    {
        var payment = new VendorPayment
        {
            PaymentNumber = "VPMT-2001",
            VendorId = vendorId,
            Method = method,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };
        if (billId is int bid && appliedAmount is decimal applied)
            payment.Applications.Add(new VendorPaymentApplication { VendorBillId = bid, Amount = applied, SettlementFxRate = settlementFxRate });

        db.Set<VendorPayment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static async Task<VendorBill> AddApprovedBillAsync(
        AppDbContext db, int vendorId, int currencyId = UsdId, decimal fxRate = 1m)
    {
        var bill = new VendorBill
        {
            BillNumber = "BILL-3001",
            VendorId = vendorId,
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = 100m, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    private static async Task<PaymentTransmission> AddTransmissionAsync(AppDbContext db, VendorPayment payment)
    {
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment",
            SourceId = payment.Id,
            Status = PaymentTransmissionStatus.Queued,
            Amount = payment.Amount,
            Method = payment.Method.ToString(),
            CreatedByUserId = CreatorUserId,
        };
        db.PaymentTransmissions.Add(transmission);
        await db.SaveChangesAsync();
        return transmission;
    }

    private static async Task<decimal> NetCitAsync(AppDbContext db)
        => (await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .Where(l => l.GlAccountId == CashInTransitId)
            .Sum(l => l.Credit - l.Debit);

    [Fact]
    public async Task Success_WithCitOrigination_PostsSettlement_NetCitZero()
    {
        var (db, vendorId) = await SeedAsync();
        var payment = await AddPaymentAsync(db, vendorId, amount: 250m); // no applications → Dr PREPAID / Cr CIT
        await OriginationService(db).PostVendorPaymentCreatedAsync(payment.Id, CreatorUserId);
        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);

        var settlement = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"AP:VendorPayment:{payment.Id}:SETTLEMENT");
        settlement.Source.Should().Be(JournalSource.AP);
        settlement.SourceType.Should().Be("VendorPayment");
        settlement.SourceId.Should().Be(payment.Id);
        settlement.EntryDate.Should().Be(new DateOnly(2026, 6, 11)); // clock-driven settlement date
        settlement.Memo.Should().Contain(payment.PaymentNumber);
        settlement.PostedBy.Should().Be(CreatorUserId);
        settlement.Lines.Single(l => l.GlAccountId == CashInTransitId).Debit.Should().Be(250m);
        settlement.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(250m);

        // The payment's in-transit balance is fully cleared: origination Cr 250 + settlement Dr 250.
        (await NetCitAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Success_NoOriginationJe_SkipsSettlementSilently()
    {
        // FULLGL was off when the payment was created → no origination entry → nothing to settle.
        var (db, vendorId) = await SeedAsync();
        var payment = await AddPaymentAsync(db, vendorId, amount: 250m);
        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Success_LegacyOriginationWithoutCit_SkipsSettlement()
    {
        // An origination that credited CASH directly (pre-CIT electronic payment) has nothing in transit.
        var (db, vendorId) = await SeedAsync();
        var payment = await AddPaymentAsync(db, vendorId, amount: 250m);
        await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = new DateOnly(2026, 6, 1),
            Source = JournalSource.AP,
            SourceType = "VendorPayment",
            SourceId = payment.Id,
            CurrencyId = UsdId,
            IdempotencyKey = $"AP:VendorPayment:{payment.Id}:PAYMENT",
            Lines =
            [
                new PostingLine { AccountKey = "PREPAID_EXPENSE", Debit = 250m },
                new PostingLine { AccountKey = "CASH", Credit = 250m },
            ],
        }, CreatorUserId);
        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        // Only the legacy origination exists — no settlement was posted.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Success_JobReRun_SettlementIsIdempotent()
    {
        var (db, vendorId) = await SeedAsync();
        var payment = await AddPaymentAsync(db, vendorId, amount: 250m);
        await OriginationService(db).PostVendorPaymentCreatedAsync(payment.Id, CreatorUserId);
        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        // Simulate a duplicate/re-enqueued run reaching the success path again (e.g. sweep double-enqueue
        // racing a manual retry): the engine's (BookId, IdempotencyKey) de-dupe absorbs the re-post.
        transmission.Status = PaymentTransmissionStatus.Queued;
        await db.SaveChangesAsync();
        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        (await db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(e => e.IdempotencyKey == $"AP:VendorPayment:{payment.Id}:SETTLEMENT"))
            .Should().Be(1);
        (await NetCitAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Success_RealizedFxOrigination_SettlesInTransitAmount()
    {
        // EUR bill foreign 100 booked @1.10 (AP 110), settled electronically @1.05 → origination
        // Dr AP 110 / Cr CIT 105 / Cr FX_GAIN 5. Settlement must clear exactly 105 (the in-transit
        // functional cash), NOT the 110 AP relief.
        var (db, vendorId) = await SeedAsync();
        var bill = await AddApprovedBillAsync(db, vendorId, currencyId: EurId, fxRate: 1.10m);
        var payment = await AddPaymentAsync(
            db, vendorId, amount: 105m, billId: bill.Id, appliedAmount: 100m, settlementFxRate: 1.05m);
        await OriginationService(db).PostVendorPaymentCreatedAsync(payment.Id, CreatorUserId);
        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        var settlement = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"AP:VendorPayment:{payment.Id}:SETTLEMENT");
        settlement.Lines.Single(l => l.GlAccountId == CashInTransitId).Debit.Should().Be(105m);
        settlement.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(105m);
        (await NetCitAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Success_SettlementPostingFailure_DoesNotFailTransmission()
    {
        var (db, vendorId) = await SeedAsync();
        var payment = await AddPaymentAsync(db, vendorId, amount: 250m);
        await OriginationService(db).PostVendorPaymentCreatedAsync(payment.Id, CreatorUserId);

        // Sabotage the settlement: drop the CASH determination rule so the engine can't resolve it.
        var cashRule = await db.Set<AccountDeterminationRule>().SingleAsync(r => r.Key == "CASH");
        db.Set<AccountDeterminationRule>().Remove(cashRule);
        await db.SaveChangesAsync();

        var transmission = await AddTransmissionAsync(db, payment);

        await BuildJob(db, BankSucceeding()).ProcessAsync(transmission.Id, CancellationToken.None);

        // The submission DID succeed; the settlement failure is logged, not propagated.
        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        (await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.IdempotencyKey == $"AP:VendorPayment:{payment.Id}:SETTLEMENT"))
            .Should().BeFalse();
        // The lingering CIT credit IS the visible reconciling item.
        (await NetCitAsync(db)).Should().Be(250m);
    }
}
