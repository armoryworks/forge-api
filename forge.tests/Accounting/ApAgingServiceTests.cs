using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// AP-001 — AP aging derived from the OPEN-ITEM sub-ledger (the open-item cutover, mirror of
/// <see cref="ArAgingServiceTests"/>): per vendor, each non-Closed/non-Voided bill's open
/// functional remainder, bucketed by the age of its DueDate (BillDate fallback) — document-grain
/// aging. Items are created/applied by the REAL posting services, so these tests also prove the
/// control-vs-open-items reconciliation ties exactly after post + partial-pay (incl. the FX
/// booking-rate case), that a voided bill counts on neither side, and that an out-of-band AP
/// control posting surfaces as a reconciliation difference.
/// </summary>
public class ApAgingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;

    private const int ApControlId = 102;
    private const int ExpenseId = 101;
    private const int CashId = 100;
    private const int FxGainId = 106;
    private const int FxLossId = 107;

    private const int OpenPeriodId = 1000;

    private const int PriorFiscalYearId = 9;
    private const int PriorPeriodId = 999;

    private const int VendorAId = 8001;
    private const int VendorBId = 8002;

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

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly DateOnly AsOf = new(2026, 4, 1);

    private static IClock ClockAsOf()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), ClockAsOf());

    private static ApAgingService Service(AppDbContext db)
        => new(db, ClockAsOf());

    private static VendorBillApPostingService BillService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn());

    private static VendorPaymentCashPostingService PaymentService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn(), clock: ClockAsOf());

    private static async Task<AppDbContext> SeedAsync()
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

        db.Set<FiscalYear>().AddRange(
            new FiscalYear
            {
                Id = PriorFiscalYearId, BookId = BookId, Name = "FY2025",
                StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
                Status = FiscalYearStatus.Open,
            },
            new FiscalYear
            {
                Id = FiscalYearId, BookId = BookId, Name = "FY2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                Status = FiscalYearStatus.Open,
            });
        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod
            {
                Id = PriorPeriodId, FiscalYearId = PriorFiscalYearId, PeriodNumber = 1, Name = "FY2025",
                StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31),
                Status = FiscalPeriodStatus.Open,
            },
            new FiscalPeriod
            {
                Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                Status = FiscalPeriodStatus.Open,
            });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });

        db.Set<Vendor>().AddRange(
            new Vendor { Id = VendorAId, CompanyName = "Acme Supply", IsActive = true },
            new Vendor { Id = VendorBId, CompanyName = "Beta Supply", IsActive = true });

        await db.SaveChangesAsync();
        return db;
    }

    private static DateTimeOffset At(DateOnly d)
        => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// Creates and POSTS an approved vendor bill via the real AP posting service: Dr Expense /
    /// Cr AP (party = vendor) + the bill's ApOpenItem. Due date defaults to the bill date.
    /// </summary>
    private static async Task<VendorBill> PostBillAsync(
        AppDbContext db, int vendorId, decimal amount, DateOnly billDate,
        DateOnly? dueDate = null, int currencyId = UsdId, decimal fxRate = 1m,
        bool noDueDate = false)
    {
        var bill = new VendorBill
        {
            BillNumber = $"BILL-{Guid.NewGuid():N}"[..13],
            VendorId = vendorId,
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = VendorBillStatus.Approved,
            BillDate = At(billDate),
            DueDate = noDueDate ? default : At(dueDate ?? billDate),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = amount, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();

        await BillService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        return bill;
    }

    /// <summary>
    /// Creates and POSTS a vendor payment applying <paramref name="appliedForeign"/> to the bill
    /// via the real cash posting service (relieves AP at the bill's booking rate + applies the item).
    /// </summary>
    private static async Task<VendorPayment> PostPaymentAsync(
        AppDbContext db, VendorBill bill, decimal appliedForeign, DateOnly paymentDate,
        decimal settlementFxRate = 1m)
    {
        var payment = new VendorPayment
        {
            PaymentNumber = $"VPMT-{Guid.NewGuid():N}"[..13],
            VendorId = bill.VendorId,
            Method = PaymentMethod.Check,
            Amount = Math.Round(appliedForeign * settlementFxRate, 2, MidpointRounding.AwayFromZero),
            PaymentDate = At(paymentDate),
            Applications = [new VendorPaymentApplication { VendorBillId = bill.Id, Amount = appliedForeign, SettlementFxRate = settlementFxRate }],
        };
        db.Set<VendorPayment>().Add(payment);
        await db.SaveChangesAsync();

        await PaymentService(db).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        return payment;
    }

    [Fact]
    public async Task Aging_BucketsByDueDate_PerVendor()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20)); // 12 days → 0-30
        await PostBillAsync(db, VendorAId, 200m, new DateOnly(2026, 2, 20)); // 40 days → 31-60
        await PostBillAsync(db, VendorAId, 400m, new DateOnly(2026, 1, 20)); // 71 days → 61-90
        await PostBillAsync(db, VendorAId, 800m, new DateOnly(2025, 12, 1)); // 121 days → 91+
        await PostBillAsync(db, VendorBId, 50m, new DateOnly(2026, 3, 31)); //  1 day → 0-30

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.AsOfDate.Should().Be(AsOf);
        aging.GrandTotal.Should().Be(1550m);

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        a.VendorName.Should().Be("Acme Supply");
        a.OpenBalance.Should().Be(1500m);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(100m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "61-90").Amount.Should().Be(400m);
        a.Buckets.Single(b => b.Label == "91+").Amount.Should().Be(800m);

        var b = aging.Vendors.Single(v => v.VendorId == VendorBId);
        b.OpenBalance.Should().Be(50m);
        b.Buckets.Single(bk => bk.Label == "0-30").Amount.Should().Be(50m);

        aging.TotalsByBucket.Single(b => b.Label == "0-30").Amount.Should().Be(150m);
        aging.TotalsByBucket.Single(b => b.Label == "91+").Amount.Should().Be(800m);

        aging.Vendors.Select(v => v.VendorId).Should().ContainInOrder(VendorAId, VendorBId);

        // Items maintained inside the posting transactions → ties by construction.
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_PartialPayment_ShrinksTheDocumentBucket_DocumentGrain()
    {
        using var db = await SeedAsync();
        var bill = await PostBillAsync(db, VendorAId, 300m, new DateOnly(2026, 3, 1)); // 31 days → 31-60
        await PostPaymentAsync(db, bill, 100m, new DateOnly(2026, 3, 25));

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        // Document-grain semantics: the bill's open remainder (200) stays in ITS bucket — the
        // payment shrinks the document, it does not debit a younger bucket (the old
        // balance-forward behavior put −100 in 0-30).
        a.OpenBalance.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(0m);
        a.OpenBalance.Should().Be(a.Buckets.Sum(b => b.Amount));
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_NoDueDate_FallsBackToBillDate()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 2, 20), noDueDate: true); // 40 days → 31-60

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Aging_FullyPaidVendor_DroppedFromReport()
    {
        using var db = await SeedAsync();
        var bill = await PostBillAsync(db, VendorAId, 250m, new DateOnly(2026, 3, 1));
        await PostPaymentAsync(db, bill, 250m, new DateOnly(2026, 3, 20));

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.Vendors.Should().NotContain(v => v.VendorId == VendorAId);
        aging.GrandTotal.Should().Be(0m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_OnlyAgesDocumentsOnOrBeforeAsOf()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20)); // before AsOf
        await PostBillAsync(db, VendorAId, 999m, new DateOnly(2026, 4, 15)); // AFTER AsOf → excluded

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.Vendors.Single(v => v.VendorId == VendorAId).OpenBalance.Should().Be(100m);
    }

    [Fact]
    public async Task Aging_VoidedBill_ExcludedFromAging_AndStillReconciles()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));
        var voided = await PostBillAsync(db, VendorAId, 400m, new DateOnly(2026, 3, 10));
        await BillService(db).ReverseVendorBillApprovedAsync(voided.Id, reversedByUserId: 7);

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        // The voided bill's GL nets to zero and its Voided item counts on neither side.
        aging.Vendors.Single(v => v.VendorId == VendorAId).OpenBalance.Should().Be(100m);
        aging.Reconciliation.ControlBalance.Should().Be(100m);
        aging.Reconciliation.AgingTotal.Should().Be(100m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_TiesToApControlBalance_AfterPostAndPartialPay()
    {
        using var db = await SeedAsync();
        var billA = await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));
        await PostBillAsync(db, VendorBId, 250m, new DateOnly(2026, 2, 10));
        await PostPaymentAsync(db, billA, 40m, new DateOnly(2026, 3, 28));

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        // Control = 100 + 250 − 40 = 310; Σ open items = 60 + 250 = 310.
        recon.ControlBalance.Should().Be(310m);
        recon.AgingTotal.Should().Be(310m);
        recon.Difference.Should().Be(0m);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_FxBill_PartialPay_ReliefAtBookingRate_TiesExactly()
    {
        using var db = await SeedAsync();
        // EUR bill foreign 100 booked @1.10 → control +110, item open 110. Partial pay foreign 40
        // settled @1.05: control relieved at the BOOKING rate (44); the item moves by the same 44.
        var bill = await PostBillAsync(
            db, VendorAId, 100m, new DateOnly(2026, 2, 20), currencyId: EurId, fxRate: 1.10m);
        await PostPaymentAsync(db, bill, 40m, new DateOnly(2026, 3, 25), settlementFxRate: 1.05m);

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        // Control 110 − 44 = 66 == Σ open functional (110 − 44).
        aging.Reconciliation.ControlBalance.Should().Be(66m);
        aging.Reconciliation.AgingTotal.Should().Be(66m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        a.OpenBalance.Should().Be(66m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(66m); // due 2/20 → 40 days
    }

    [Fact]
    public async Task Reconciliation_DetectsOutOfBandApControlPosting()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));

        // An out-of-band/manual posting straight to AP control bypasses the open items BY DESIGN —
        // the reconciliation difference is what surfaces it.
        var entry = new JournalEntry
        {
            BookId = BookId, EntryNumber = 999, EntryDate = new DateOnly(2026, 3, 15),
            FiscalPeriodId = OpenPeriodId, FiscalYearId = FiscalYearId,
            Source = JournalSource.Conversion, CurrencyId = UsdId,
            Status = JournalEntryStatus.Posted, PostedBy = 1, PostedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new JournalLine { BookId = BookId, LineNumber = 1, GlAccountId = ApControlId, Debit = 0m, Credit = 75m, CurrencyId = UsdId, TxnAmount = 75m, FunctionalAmount = 75m, FxRate = 1m },
                new JournalLine { BookId = BookId, LineNumber = 2, GlAccountId = ExpenseId, Debit = 75m, Credit = 0m, CurrencyId = UsdId, TxnAmount = 75m, FunctionalAmount = 75m, FxRate = 1m },
            ],
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        recon.ControlBalance.Should().Be(175m);
        recon.AgingTotal.Should().Be(100m);
        recon.Difference.Should().Be(75m);
        recon.IsReconciled.Should().BeFalse();
    }

    [Fact]
    public async Task Aging_IsFilterImmune_SoftDeletedVendorStillAged()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 120m, new DateOnly(2026, 3, 10));

        var vendor = await db.Set<Vendor>().FirstAsync(v => v.Id == VendorAId);
        vendor.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        a.OpenBalance.Should().Be(120m);
        a.VendorName.Should().Be("Acme Supply");
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_NoApActivity_EmptyAndReconciled()
    {
        using var db = await SeedAsync();

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.Vendors.Should().BeEmpty();
        aging.GrandTotal.Should().Be(0m);
        aging.Reconciliation.ControlBalance.Should().Be(0m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();
        aging.TotalsByBucket.Select(b => b.Label).Should().ContainInOrder("0-30", "31-60", "61-90", "91+");
    }

    [Fact]
    public async Task Aging_DefaultsAsOfToClockToday()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));

        var aging = await Service(db).GetApAgingAsync(BookId);

        aging.AsOfDate.Should().Be(AsOf);
        aging.Vendors.Single(v => v.VendorId == VendorAId).OpenBalance.Should().Be(100m);
    }
}
