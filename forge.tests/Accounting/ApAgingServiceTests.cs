using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE A — AP sub-ledger + aging (the AP counterpart of <see cref="ArAgingServiceTests"/>).
/// AP is credit-normal, so a bill (Cr AP control) is a positive open payable and a payment (Dr AP) relieves
/// it — the netting is credit-positive (mirror-image of AR). Ages by each posting's EntryDate; reconciles
/// the vendor-attributed slice to the full AP-control balance; filter-immune.
/// </summary>
public class ApAgingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int ApControlId = 102;
    private const int ExpenseId = 101;
    private const int CashId = 100;

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

    private static readonly DateOnly AsOf = new(2026, 4, 1);

    private static IClock ClockAsOf()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), ClockAsOf());

    private static ApAgingService Service(AppDbContext db)
        => new(db, ClockAsOf());

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

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
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId });

        db.Set<Vendor>().AddRange(
            new Vendor { Id = VendorAId, CompanyName = "Acme Supply", IsActive = true },
            new Vendor { Id = VendorBId, CompanyName = "Beta Supply", IsActive = true });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Posts Dr Expense / Cr AP (party = vendor) — a bill raises the payable (credit-positive).</summary>
    private static Task PostBillAsync(AppDbContext db, int vendorId, decimal amount, DateOnly entryDate)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "Bill",
            SourceId = vendorId * 1000 + entryDate.DayOfYear,
            CurrencyId = UsdId,
            IdempotencyKey = $"AP:Bill:{vendorId}:{entryDate:yyyyMMdd}:EXPENSE",
            Lines =
            [
                new PostingLine { AccountKey = "OPERATING_EXPENSE", Debit = amount },
                new PostingLine { AccountKey = "AP_CONTROL", PartyType = SubledgerPartyType.Vendor, PartyId = vendorId, Credit = amount },
            ],
        }, postedByUserId: 7);

    /// <summary>Posts Dr AP (party = vendor) / Cr Cash — a payment relieves the payable.</summary>
    private static Task PostPaymentAsync(AppDbContext db, int vendorId, decimal amount, DateOnly entryDate)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = entryDate,
            Source = JournalSource.AP,
            SourceType = "Payment",
            SourceId = vendorId * 1000 + entryDate.DayOfYear + 500000,
            CurrencyId = UsdId,
            IdempotencyKey = $"AP:Payment:{vendorId}:{entryDate:yyyyMMdd}:PAYMENT",
            Lines =
            [
                new PostingLine { AccountKey = "AP_CONTROL", PartyType = SubledgerPartyType.Vendor, PartyId = vendorId, Debit = amount },
                new PostingLine { AccountKey = "CASH", Credit = amount },
            ],
        }, postedByUserId: 7);

    [Fact]
    public async Task Aging_BucketsByAge_PerVendor()
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
    }

    [Fact]
    public async Task Aging_PaymentRelievesOpenBalance()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 300m, new DateOnly(2026, 3, 1)); // 31 days → 31-60
        await PostPaymentAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 25)); // 7 days → 0-30 (debit)

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        var a = aging.Vendors.Single(v => v.VendorId == VendorAId);
        a.OpenBalance.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(-100m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(300m);
        a.OpenBalance.Should().Be(a.Buckets.Sum(b => b.Amount));
    }

    [Fact]
    public async Task Aging_FullyPaidVendor_DroppedFromReport()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 250m, new DateOnly(2026, 3, 1));
        await PostPaymentAsync(db, VendorAId, 250m, new DateOnly(2026, 3, 20));

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.Vendors.Should().NotContain(v => v.VendorId == VendorAId);
        aging.GrandTotal.Should().Be(0m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_OnlyAgesEntriesOnOrBeforeAsOf()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20)); // before AsOf
        await PostBillAsync(db, VendorAId, 999m, new DateOnly(2026, 4, 15)); // AFTER AsOf → excluded

        var aging = await Service(db).GetApAgingAsync(BookId, AsOf);

        aging.Vendors.Single(v => v.VendorId == VendorAId).OpenBalance.Should().Be(100m);
    }

    [Fact]
    public async Task Reconciliation_TiesToApControlBalance()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));
        await PostBillAsync(db, VendorBId, 250m, new DateOnly(2026, 2, 10));
        await PostPaymentAsync(db, VendorAId, 40m, new DateOnly(2026, 3, 28));

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        recon.ControlBalance.Should().Be(310m);
        recon.AgingTotal.Should().Be(310m);
        recon.Difference.Should().Be(0m);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_DetectsApControlPostingMissingVendorParty()
    {
        using var db = await SeedAsync();
        await PostBillAsync(db, VendorAId, 100m, new DateOnly(2026, 3, 20));

        // Simulate a defect: an AP-control line with NO vendor party (out-of-band write). The orphan
        // sits on the CREDIT side of AP control so it lands in the credit-positive control balance.
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
