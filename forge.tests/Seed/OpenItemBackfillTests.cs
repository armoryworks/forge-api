using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Data;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Covers the boot-time open-item backfill <c>SeedData.EnsureOpenItemsBackfilledAsync</c>
/// (AR-002/AP-001 — mirrors the EnsureCashInTransitAsync ensure pattern): when a Book exists,
/// posted AR/AP origination journals exist, and an open-item table is EMPTY, items are rebuilt
/// from the operational Invoices / VendorBills matched by the posting idempotency keys —
/// applied amounts from their applications (only those whose payment origination actually
/// POSTED, functional at the document booking rate), statuses recomputed, a Reversed bill
/// origination → Voided item. Idempotent via the empty-table guard.
/// </summary>
public class OpenItemBackfillTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int ApControlId = 200;
    private const int ExpenseId = 201;
    private const int FxGainId = 106;
    private const int FxLossId = 107;

    private static readonly DateTimeOffset Now = new(2026, 1, 25, 12, 0, 0, TimeSpan.Zero);

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

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock(Now));

    private static InvoiceArPostingService InvoiceService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn());

    private static PaymentCashPostingService PaymentService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn(), clock: new FixedClock(Now));

    private static VendorBillApPostingService BillService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn());

    private static VendorPaymentCashPostingService VendorPaymentService(AppDbContext db)
        => new(db, Engine(db), new FullGlOn(), clock: new FixedClock(Now));

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

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Invoice> PostInvoiceAsync(
        AppDbContext db, decimal amount, int currencyId = UsdId, decimal fxRate = 1m)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}"[..12],
            CustomerId = customer.Id,
            CurrencyId = currencyId,
            FxRate = fxRate,
            InvoiceDate = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Sent,
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = amount, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        return invoice;
    }

    private static async Task<Payment> AddPaymentAsync(
        AppDbContext db, Invoice invoice, decimal appliedForeign, decimal settlementFxRate = 1m,
        bool post = true)
    {
        var payment = new Payment
        {
            PaymentNumber = $"PMT-{Guid.NewGuid():N}"[..12],
            CustomerId = invoice.CustomerId,
            Method = PaymentMethod.Check,
            Amount = Math.Round(appliedForeign * settlementFxRate, 2, MidpointRounding.AwayFromZero),
            PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Applications = [new PaymentApplication { InvoiceId = invoice.Id, Amount = appliedForeign, SettlementFxRate = settlementFxRate }],
        };
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();

        if (post)
            await PaymentService(db).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        return payment;
    }

    private static async Task<VendorBill> PostBillAsync(AppDbContext db, decimal amount)
    {
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();

        var bill = new VendorBill
        {
            BillNumber = $"BILL-{Guid.NewGuid():N}"[..13],
            VendorId = vendor.Id,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            Lines = [new VendorBillLine { Description = "Steel", Quantity = 1m, UnitPrice = amount, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();

        await BillService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        return bill;
    }

    /// <summary>Simulates a pre-open-item install: posted journals exist, but the item tables are empty.</summary>
    private static async Task WipeOpenItemsAsync(AppDbContext db)
    {
        db.ArOpenItems.RemoveRange(await db.ArOpenItems.ToListAsync());
        db.ApOpenItems.RemoveRange(await db.ApOpenItems.ToListAsync());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Backfill_RebuildsArItems_WithBookingRateAppliedAmounts()
    {
        using var db = await SeedAsync();
        // EUR invoice 100 @1.10, partially paid 40 foreign @ settlement 1.05; USD invoice 200 fully paid.
        var eurInvoice = await PostInvoiceAsync(db, 100m, currencyId: EurId, fxRate: 1.10m);
        await AddPaymentAsync(db, eurInvoice, 40m, settlementFxRate: 1.05m);
        var usdInvoice = await PostInvoiceAsync(db, 200m);
        await AddPaymentAsync(db, usdInvoice, 200m);

        await WipeOpenItemsAsync(db);

        await SeedData.EnsureOpenItemsBackfilledAsync(db);

        var items = await db.ArOpenItems.ToListAsync();
        items.Should().HaveCount(2);

        var eur = items.Single(i => i.SourceId == eurInvoice.Id);
        eur.DocumentNumber.Should().Be(eurInvoice.InvoiceNumber);
        eur.CurrencyId.Should().Be(EurId);
        eur.FxRate.Should().Be(1.10m);
        eur.OriginalTxnAmount.Should().Be(100m);
        eur.OriginalFunctionalAmount.Should().Be(110m);
        eur.AppliedTxnAmount.Should().Be(40m);
        eur.AppliedFunctionalAmount.Should().Be(44m); // booking rate — never settlement
        eur.Status.Should().Be(OpenItemStatus.PartiallyApplied);

        var usd = items.Single(i => i.SourceId == usdInvoice.Id);
        usd.AppliedTxnAmount.Should().Be(200m);
        usd.Status.Should().Be(OpenItemStatus.Closed);

        // The reconstructed sub-ledger ties to control again.
        var recon = await new ArAgingService(db, new FixedClock(Now)).ReconcileAsync(BookId);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_IgnoresApplications_WhosePaymentNeverPosted()
    {
        using var db = await SeedAsync();
        var invoice = await PostInvoiceAsync(db, 100m);
        // The payment was recorded while FULLGL was OFF — it never moved AR control,
        // so its application must NOT shrink the rebuilt item (control would no longer tie).
        await AddPaymentAsync(db, invoice, 100m, post: false);

        await WipeOpenItemsAsync(db);

        await SeedData.EnsureOpenItemsBackfilledAsync(db);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(0m);
        item.Status.Should().Be(OpenItemStatus.Open);

        var recon = await new ArAgingService(db, new FixedClock(Now)).ReconcileAsync(BookId);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_RebuildsApItems_InclVoidedBill()
    {
        using var db = await SeedAsync();
        var openBill = await PostBillAsync(db, 100m);
        var voidedBill = await PostBillAsync(db, 400m);
        await BillService(db).ReverseVendorBillApprovedAsync(voidedBill.Id, reversedByUserId: 7);
        var paidBill = await PostBillAsync(db, 200m);
        var payment = new VendorPayment
        {
            PaymentNumber = "VPMT-BF-1",
            VendorId = paidBill.VendorId,
            Method = PaymentMethod.Check,
            Amount = 200m,
            PaymentDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            Applications = [new VendorPaymentApplication { VendorBillId = paidBill.Id, Amount = 200m }],
        };
        db.Set<VendorPayment>().Add(payment);
        await db.SaveChangesAsync();
        await VendorPaymentService(db).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        await WipeOpenItemsAsync(db);

        await SeedData.EnsureOpenItemsBackfilledAsync(db);

        var items = await db.ApOpenItems.ToListAsync();
        items.Should().HaveCount(3);
        items.Single(i => i.SourceId == openBill.Id).Status.Should().Be(OpenItemStatus.Open);
        // Reversed origination (voided bill) → Voided item, like the posting-time void path.
        items.Single(i => i.SourceId == voidedBill.Id).Status.Should().Be(OpenItemStatus.Voided);
        var paid = items.Single(i => i.SourceId == paidBill.Id);
        paid.AppliedTxnAmount.Should().Be(200m);
        paid.Status.Should().Be(OpenItemStatus.Closed);

        var recon = await new ApAgingService(db, new FixedClock(Now)).ReconcileAsync(BookId);
        recon.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_EmptyTableGuard_NeverRunsOverPostingMaintainedItems()
    {
        using var db = await SeedAsync();
        var invoice = await PostInvoiceAsync(db, 100m);
        await AddPaymentAsync(db, invoice, 40m);
        // Items exist (posting-time maintenance owns them) → the backfill must be a no-op,
        // even on repeated boots.
        await SeedData.EnsureOpenItemsBackfilledAsync(db);
        await SeedData.EnsureOpenItemsBackfilledAsync(db);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(40m); // unchanged — not re-applied or duplicated
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);
    }

    [Fact]
    public async Task Backfill_NoBook_OrNoJournals_IsNoOp()
    {
        // No Book at all → returns immediately.
        using var empty = TestDbContextFactory.Create();
        await SeedData.EnsureOpenItemsBackfilledAsync(empty);
        (await empty.ArOpenItems.AnyAsync()).Should().BeFalse();

        // Book but no posted originations → nothing to rebuild (and no spurious save).
        using var db = await SeedAsync();
        await SeedData.EnsureOpenItemsBackfilledAsync(db);
        (await db.ArOpenItems.AnyAsync()).Should().BeFalse();
        (await db.ApOpenItems.AnyAsync()).Should().BeFalse();
    }
}
