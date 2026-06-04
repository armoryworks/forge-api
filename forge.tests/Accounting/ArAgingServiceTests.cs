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
/// Phase-1 STAGE D — AR sub-ledger + aging derived from the ledger
/// (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "AR sub-ledger + aging", §9
/// "sub-ledger↔control reconciliation"). Proves the aging is projected from
/// posted AR-control <see cref="JournalLine"/>s carrying a Customer party,
/// bucketed by the age of each posting's EntryDate, that a payment relieves the
/// open balance, that the AR-control-vs-aging reconciliation ties (and detects a
/// missing-party defect), and that the read is filter-immune.
/// </summary>
public class ArAgingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int ArControlId = 102;
    private const int RevenueId = 101;
    private const int CashId = 100;

    private const int OpenPeriodId = 1000;

    private const int PriorFiscalYearId = 9;
    private const int PriorPeriodId = 999;

    private const int CustomerAId = 7001;
    private const int CustomerBId = 7002;

    /// <summary>Fixed clock so "today" / age buckets are deterministic.</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    /// <summary>In-process allocator (InMemory can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    // As-of date the aging is computed against in these tests.
    private static readonly DateOnly AsOf = new(2026, 4, 1);

    private static IClock ClockAsOf()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), ClockAsOf());

    private static ArAgingService Service(AppDbContext db)
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
        // One Open period spanning each whole year so any EntryDate resolves
        // (the 91+ aging bucket needs an entry dated in late 2025).
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
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId });

        db.Set<Customer>().AddRange(
            new Customer { Id = CustomerAId, Name = "Acme Corp" },
            new Customer { Id = CustomerBId, Name = "Beta LLC" });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Posts Dr AR / Cr Revenue for a customer dated <paramref name="entryDate"/>.</summary>
    private static Task PostInvoiceAsync(AppDbContext db, int customerId, decimal amount, DateOnly entryDate)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = entryDate,
            Source = JournalSource.AR,
            SourceType = "Invoice",
            SourceId = customerId * 1000 + entryDate.DayOfYear,
            CurrencyId = UsdId,
            IdempotencyKey = $"AR:Invoice:{customerId}:{entryDate:yyyyMMdd}:REVENUE",
            Lines =
            [
                new PostingLine { AccountKey = "AR_CONTROL", PartyType = SubledgerPartyType.Customer, PartyId = customerId, Debit = amount },
                new PostingLine { AccountKey = "SALES_REVENUE", Credit = amount },
            ],
        }, postedByUserId: 7);

    /// <summary>Posts Dr Cash / Cr AR for a customer (relieves the receivable).</summary>
    private static Task PostPaymentAsync(AppDbContext db, int customerId, decimal amount, DateOnly entryDate)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = entryDate,
            Source = JournalSource.AR,
            SourceType = "Payment",
            SourceId = customerId * 1000 + entryDate.DayOfYear + 500000,
            CurrencyId = UsdId,
            IdempotencyKey = $"AR:Payment:{customerId}:{entryDate:yyyyMMdd}:PAYMENT",
            Lines =
            [
                new PostingLine { AccountKey = "CASH", Debit = amount },
                new PostingLine { AccountKey = "AR_CONTROL", PartyType = SubledgerPartyType.Customer, PartyId = customerId, Credit = amount },
            ],
        }, postedByUserId: 7);

    [Fact]
    public async Task Aging_BucketsByAge_PerCustomer()
    {
        using var db = await SeedAsync();
        // Customer A: invoices at increasing age relative to AsOf (2026-04-01).
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20)); // 12 days → 0-30
        await PostInvoiceAsync(db, CustomerAId, 200m, new DateOnly(2026, 2, 20)); // 40 days → 31-60
        await PostInvoiceAsync(db, CustomerAId, 400m, new DateOnly(2026, 1, 20)); // 71 days → 61-90
        await PostInvoiceAsync(db, CustomerAId, 800m, new DateOnly(2025, 12, 1)); // 121 days → 91+
        // Customer B: a single recent invoice.
        await PostInvoiceAsync(db, CustomerBId, 50m, new DateOnly(2026, 3, 31)); //  1 day → 0-30

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        aging.AsOfDate.Should().Be(AsOf);
        aging.GrandTotal.Should().Be(1550m);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        a.CustomerName.Should().Be("Acme Corp");
        a.OpenBalance.Should().Be(1500m);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(100m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "61-90").Amount.Should().Be(400m);
        a.Buckets.Single(b => b.Label == "91+").Amount.Should().Be(800m);

        var b = aging.Customers.Single(c => c.CustomerId == CustomerBId);
        b.OpenBalance.Should().Be(50m);
        b.Buckets.Single(bk => bk.Label == "0-30").Amount.Should().Be(50m);

        // Rolled-up bucket totals across customers.
        aging.TotalsByBucket.Single(b => b.Label == "0-30").Amount.Should().Be(150m);
        aging.TotalsByBucket.Single(b => b.Label == "91+").Amount.Should().Be(800m);

        // Customers ordered by descending open balance (A before B).
        aging.Customers.Select(c => c.CustomerId).Should().ContainInOrder(CustomerAId, CustomerBId);
    }

    [Fact]
    public async Task Aging_PaymentRelievesOpenBalance()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 300m, new DateOnly(2026, 3, 1)); // 31 days → 31-60
        await PostPaymentAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 25)); // 7 days → 0-30 (credit)

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        // Net open = 300 invoice − 100 payment = 200.
        a.OpenBalance.Should().Be(200m);
        // The credit lands in the 0-30 bucket where the payment is dated.
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(-100m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(300m);
        a.OpenBalance.Should().Be(a.Buckets.Sum(b => b.Amount));
    }

    [Fact]
    public async Task Aging_FullyPaidCustomer_DroppedFromReport()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 250m, new DateOnly(2026, 3, 1));
        await PostPaymentAsync(db, CustomerAId, 250m, new DateOnly(2026, 3, 20));

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        // Net zero → not an open item → absent from the customer rows.
        aging.Customers.Should().NotContain(c => c.CustomerId == CustomerAId);
        aging.GrandTotal.Should().Be(0m);
        // But the reconciliation still ties to the (zero) control balance.
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_OnlyAgesEntriesOnOrBeforeAsOf()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20)); // before AsOf
        await PostInvoiceAsync(db, CustomerAId, 999m, new DateOnly(2026, 4, 15)); // AFTER AsOf → excluded

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        aging.Customers.Single(c => c.CustomerId == CustomerAId).OpenBalance.Should().Be(100m);
    }

    [Fact]
    public async Task Reconciliation_TiesToArControlBalance()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20));
        await PostInvoiceAsync(db, CustomerBId, 250m, new DateOnly(2026, 2, 10));
        await PostPaymentAsync(db, CustomerAId, 40m, new DateOnly(2026, 3, 28));

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        // Control balance = 100 + 250 − 40 = 310; aging total equals it.
        recon.ControlBalance.Should().Be(310m);
        recon.AgingTotal.Should().Be(310m);
        recon.Difference.Should().Be(0m);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_DetectsArControlPostingMissingCustomerParty()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20));

        // Simulate a defect: an AR-control line with NO customer party slips into
        // the ledger (the engine forbids this on control lines, §5.2 — but a DB
        // import / out-of-band write could). The reconciliation must surface it.
        var entry = new JournalEntry
        {
            BookId = BookId, EntryNumber = 999, EntryDate = new DateOnly(2026, 3, 15),
            FiscalPeriodId = OpenPeriodId, FiscalYearId = FiscalYearId,
            Source = JournalSource.Conversion, CurrencyId = UsdId,
            Status = JournalEntryStatus.Posted, PostedBy = 1, PostedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new JournalLine { BookId = BookId, LineNumber = 1, GlAccountId = ArControlId, Debit = 75m, Credit = 0m, CurrencyId = UsdId, TxnAmount = 75m, FunctionalAmount = 75m, FxRate = 1m },
                new JournalLine { BookId = BookId, LineNumber = 2, GlAccountId = RevenueId, Debit = 0m, Credit = 75m, CurrencyId = UsdId, TxnAmount = 75m, FunctionalAmount = 75m, FxRate = 1m },
            ],
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        // Control balance = 100 (customer A) + 75 (orphan) = 175; aging total
        // only sees the customer-attributed 100 → a 75 difference is flagged.
        recon.ControlBalance.Should().Be(175m);
        recon.AgingTotal.Should().Be(100m);
        recon.Difference.Should().Be(75m);
        recon.IsReconciled.Should().BeFalse();
    }

    [Fact]
    public async Task Aging_IsFilterImmune_SoftDeletedCustomerStillAged()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 120m, new DateOnly(2026, 3, 10));

        // Soft-delete the customer master; its open AR must still appear.
        var customer = await db.Set<Customer>().FirstAsync(c => c.Id == CustomerAId);
        customer.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        a.OpenBalance.Should().Be(120m);
        a.CustomerName.Should().Be("Acme Corp");
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_NoArActivity_EmptyAndReconciled()
    {
        using var db = await SeedAsync();

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        aging.Customers.Should().BeEmpty();
        aging.GrandTotal.Should().Be(0m);
        aging.Reconciliation.ControlBalance.Should().Be(0m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();
        // Bucket scaffold is always present even with no activity.
        aging.TotalsByBucket.Select(b => b.Label).Should().ContainInOrder("0-30", "31-60", "61-90", "91+");
    }

    [Fact]
    public async Task Aging_DefaultsAsOfToClockToday()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20));

        // No explicit asOf → service uses the (fixed) clock = AsOf.
        var aging = await Service(db).GetArAgingAsync(BookId);

        aging.AsOfDate.Should().Be(AsOf);
        aging.Customers.Single(c => c.CustomerId == CustomerAId).OpenBalance.Should().Be(100m);
    }
}
