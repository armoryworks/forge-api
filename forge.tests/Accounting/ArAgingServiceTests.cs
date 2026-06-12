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
/// AR-002 — AR aging derived from the OPEN-ITEM sub-ledger (the open-item cutover): per
/// customer, each non-Closed/non-Voided invoice's open functional remainder, bucketed by the
/// age of its DueDate (DocumentDate fallback) — document-grain aging. Items are created /
/// applied by the REAL posting services (the same seam that moves AR control), so these tests
/// also prove the control-vs-open-items reconciliation ties exactly after post + partial-pay
/// sequences — including the FX case where relief is at the booking rate — and that a manual
/// JE hitting AR control directly surfaces as a reconciliation difference (by design).
/// </summary>
public class ArAgingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;

    private const int ArControlId = 102;
    private const int RevenueId = 101;
    private const int CashId = 100;
    private const int FxGainId = 106;
    private const int FxLossId = 107;

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

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // As-of date the aging is computed against in these tests.
    private static readonly DateOnly AsOf = new(2026, 4, 1);

    private static IClock ClockAsOf()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), ClockAsOf());

    private static ArAgingService Service(AppDbContext db)
        => new(db, ClockAsOf());

    private static InvoiceArPostingService InvoiceService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn());

    private static PaymentCashPostingService PaymentService(AppDbContext db)
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
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
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
        // (the 91+ aging bucket needs a document dated in late 2025).
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
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });

        db.Set<Customer>().AddRange(
            new Customer { Id = CustomerAId, Name = "Acme Corp" },
            new Customer { Id = CustomerBId, Name = "Beta LLC" });

        await db.SaveChangesAsync();
        return db;
    }

    private static DateTimeOffset At(DateOnly d)
        => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// Creates and POSTS an invoice via the real AR posting service: Dr AR / Cr Revenue + the
    /// invoice's ArOpenItem, in one transaction. Due date defaults to the document date.
    /// </summary>
    private static async Task<Invoice> PostInvoiceAsync(
        AppDbContext db, int customerId, decimal amount, DateOnly documentDate,
        DateOnly? dueDate = null, int currencyId = UsdId, decimal fxRate = 1m,
        bool noDueDate = false)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}"[..12],
            CustomerId = customerId,
            CurrencyId = currencyId,
            FxRate = fxRate,
            InvoiceDate = At(documentDate),
            DueDate = noDueDate ? default : At(dueDate ?? documentDate),
            Status = InvoiceStatus.Sent,
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = amount, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        return invoice;
    }

    /// <summary>
    /// Creates and POSTS a payment applying <paramref name="appliedForeign"/> to the invoice via
    /// the real cash posting service (relieves AR at the invoice's booking rate + applies the item).
    /// </summary>
    private static async Task<Payment> PostPaymentAsync(
        AppDbContext db, Invoice invoice, decimal appliedForeign, DateOnly paymentDate,
        decimal settlementFxRate = 1m)
    {
        var payment = new Payment
        {
            PaymentNumber = $"PMT-{Guid.NewGuid():N}"[..12],
            CustomerId = invoice.CustomerId,
            Method = PaymentMethod.Check,
            Amount = Math.Round(appliedForeign * settlementFxRate, 2, MidpointRounding.AwayFromZero),
            PaymentDate = At(paymentDate),
            Applications = [new PaymentApplication { InvoiceId = invoice.Id, Amount = appliedForeign, SettlementFxRate = settlementFxRate }],
        };
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();

        await PaymentService(db).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        return payment;
    }

    [Fact]
    public async Task Aging_BucketsByDueDate_PerCustomer()
    {
        using var db = await SeedAsync();
        // Customer A: invoices due at increasing age relative to AsOf (2026-04-01).
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20)); // due 12 days ago → 0-30
        await PostInvoiceAsync(db, CustomerAId, 200m, new DateOnly(2026, 2, 20)); // 40 days → 31-60
        await PostInvoiceAsync(db, CustomerAId, 400m, new DateOnly(2026, 1, 20)); // 71 days → 61-90
        await PostInvoiceAsync(db, CustomerAId, 800m, new DateOnly(2025, 12, 1)); // 121 days → 91+
        // Customer B: a single recent invoice.
        await PostInvoiceAsync(db, CustomerBId, 50m, new DateOnly(2026, 3, 31)); // 1 day → 0-30

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

        // Items maintained inside the posting transactions → ties by construction.
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_PartialPayment_ShrinksTheDocumentBucket_DocumentGrain()
    {
        using var db = await SeedAsync();
        var invoice = await PostInvoiceAsync(db, CustomerAId, 300m, new DateOnly(2026, 3, 1)); // 31 days → 31-60
        await PostPaymentAsync(db, invoice, 100m, new DateOnly(2026, 3, 25));

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        // Document-grain semantics: the invoice's open remainder (200) stays in ITS bucket —
        // the payment shrinks the document, it does not credit a younger bucket (the old
        // balance-forward behavior put −100 in 0-30).
        a.OpenBalance.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(200m);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(0m);
        a.OpenBalance.Should().Be(a.Buckets.Sum(b => b.Amount));
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_NotYetDueDocument_LandsInYoungestBucket()
    {
        using var db = await SeedAsync();
        // Invoiced 3/20, due 4/20 — after the as-of: not yet due → youngest bucket.
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20), dueDate: new DateOnly(2026, 4, 20));

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        a.Buckets.Single(b => b.Label == "0-30").Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Aging_NoDueDate_FallsBackToDocumentDate()
    {
        using var db = await SeedAsync();
        // No due date on the document → the DocumentDate anchors the age (40 days → 31-60).
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 2, 20), noDueDate: true);

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Aging_FullyPaidCustomer_DroppedFromReport()
    {
        using var db = await SeedAsync();
        var invoice = await PostInvoiceAsync(db, CustomerAId, 250m, new DateOnly(2026, 3, 1));
        await PostPaymentAsync(db, invoice, 250m, new DateOnly(2026, 3, 20));

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        // Item Closed → not an open row.
        aging.Customers.Should().NotContain(c => c.CustomerId == CustomerAId);
        aging.GrandTotal.Should().Be(0m);
        // The reconciliation still ties to the (zero) control balance.
        aging.Reconciliation.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Aging_OnlyAgesDocumentsOnOrBeforeAsOf()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20)); // before AsOf
        await PostInvoiceAsync(db, CustomerAId, 999m, new DateOnly(2026, 4, 15)); // AFTER AsOf → excluded

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        aging.Customers.Single(c => c.CustomerId == CustomerAId).OpenBalance.Should().Be(100m);
    }

    [Fact]
    public async Task Reconciliation_TiesToArControlBalance_AfterPostAndPartialPay()
    {
        using var db = await SeedAsync();
        var invoiceA = await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20));
        await PostInvoiceAsync(db, CustomerBId, 250m, new DateOnly(2026, 2, 10));
        await PostPaymentAsync(db, invoiceA, 40m, new DateOnly(2026, 3, 28));

        var recon = await Service(db).ReconcileAsync(BookId, AsOf);

        // Control balance = 100 + 250 − 40 = 310; Σ open items = 60 + 250 = 310.
        recon.ControlBalance.Should().Be(310m);
        recon.AgingTotal.Should().Be(310m);
        recon.Difference.Should().Be(0m);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_FxInvoice_PartialPay_ReliefAtBookingRate_TiesExactly()
    {
        using var db = await SeedAsync();
        // EUR invoice foreign 100 booked @1.10 → control +110, item open 110.
        var invoice = await PostInvoiceAsync(
            db, CustomerAId, 100m, new DateOnly(2026, 2, 20), currencyId: EurId, fxRate: 1.10m);
        // Partial pay foreign 40 settled @1.05: control relieved at the BOOKING rate (44), the
        // settlement difference goes to the FX plug — the item moves by the same 44.
        await PostPaymentAsync(db, invoice, 40m, new DateOnly(2026, 3, 25), settlementFxRate: 1.05m);

        var aging = await Service(db).GetArAgingAsync(BookId, AsOf);

        // Control 110 − 44 = 66 == Σ open functional (110 − 44).
        aging.Reconciliation.ControlBalance.Should().Be(66m);
        aging.Reconciliation.AgingTotal.Should().Be(66m);
        aging.Reconciliation.IsReconciled.Should().BeTrue();

        var a = aging.Customers.Single(c => c.CustomerId == CustomerAId);
        a.OpenBalance.Should().Be(66m);
        a.Buckets.Single(b => b.Label == "31-60").Amount.Should().Be(66m); // due 2/20 → 40 days
    }

    [Fact]
    public async Task Reconciliation_DetectsManualJeHittingArControlDirectly()
    {
        using var db = await SeedAsync();
        await PostInvoiceAsync(db, CustomerAId, 100m, new DateOnly(2026, 3, 20));

        // A conversion/manual JE posted straight to AR control bypasses the open items BY DESIGN
        // (no source document → no item). The reconciliation difference is what surfaces it.
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

        // Control = 100 (invoice) + 75 (manual) = 175; items only carry the invoice's 100.
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
