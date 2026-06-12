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
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// AR-002/AP-001 — per-document open-item sub-ledger maintained at posting time, inside the
/// same transaction as the control-account journal. Proves, on the InMemory provider:
///   • invoice/bill posting creates the item with booking-rate functional amounts (incl. EUR @1.10);
///   • payment applications increment applied amounts at the BOOKING-rate relief (never settlement)
///     with status transitions Open → PartiallyApplied → Closed (incl. exact-close);
///   • payment void restores the item (decrement, floored at zero);
///   • bill void flips the item to Voided (excluded from aging + reconciliation, like the reversed GL);
///   • idempotent re-posting creates no duplicate item and never double-applies;
///   • DARK by default — FULLGL off → no items at all.
/// </summary>
public class OpenItemTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2; // 2nd currency for the booking-rate FX coverage
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    // AR accounts.
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;
    private const int SalesTaxPayableId = 104;
    private const int CustomerDepositsId = 105;
    private const int FxGainId = 106;
    private const int FxLossId = 107;

    // AP accounts.
    private const int ApControlId = 200;
    private const int OperatingExpenseId = 201;
    private const int PrepaidExpenseId = 203;

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

    private static InvoiceArPostingService InvoiceService(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static PaymentCashPostingService PaymentService(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), clock: new FixedClock(Now));

    private static VendorBillApPostingService BillService(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static VendorPaymentCashPostingService VendorPaymentService(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), clock: new FixedClock(Now));

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
            new GlAccount { Id = SalesTaxPayableId, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PrepaidExpenseId, BookId = BookId, AccountNumber = "12000", Name = "Prepaid Expenses", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = CustomerDepositsId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = PrepaidExpenseId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Adds a customer + one-line invoice (no shipment → control transferred on finalize).</summary>
    private static async Task<Invoice> AddInvoiceAsync(
        AppDbContext db, decimal amount, int currencyId = UsdId, decimal fxRate = 1m, decimal taxRate = 0m)
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
            TaxRate = taxRate,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = amount, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>Adds a payment applying <paramref name="appliedForeign"/> to <paramref name="invoice"/>.</summary>
    private static async Task<Payment> AddPaymentAsync(
        AppDbContext db, Invoice invoice, decimal appliedForeign, decimal settlementFxRate = 1m)
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
        return payment;
    }

    private static async Task<int> AddVendorAsync(AppDbContext db)
    {
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<VendorBill> AddBillAsync(
        AppDbContext db, int vendorId, decimal amount, int currencyId = UsdId, decimal fxRate = 1m,
        decimal taxAmount = 0m)
    {
        var bill = new VendorBill
        {
            BillNumber = $"BILL-{Guid.NewGuid():N}"[..13],
            VendorId = vendorId,
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            TaxAmount = taxAmount,
            Lines = [new VendorBillLine { Description = "Steel bar", Quantity = 1, UnitPrice = amount, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" }],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    private static async Task<VendorPayment> AddVendorPaymentAsync(
        AppDbContext db, VendorBill bill, decimal appliedForeign, decimal settlementFxRate = 1m)
    {
        var payment = new VendorPayment
        {
            PaymentNumber = $"VPMT-{Guid.NewGuid():N}"[..13],
            VendorId = bill.VendorId,
            Method = PaymentMethod.Check,
            Amount = Math.Round(appliedForeign * settlementFxRate, 2, MidpointRounding.AwayFromZero),
            PaymentDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
            Applications = [new VendorPaymentApplication { VendorBillId = bill.Id, Amount = appliedForeign, SettlementFxRate = settlementFxRate }],
        };
        db.Set<VendorPayment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    // ─────────────────────────── AR — item creation ───────────────────────────

    [Fact]
    public async Task ArItem_CreatedOnInvoicePosting_FunctionalAtBookingRate()
    {
        using var db = await SeedAsync();
        // EUR invoice, foreign 100 booked @1.10 → functional 110.
        var invoice = await AddInvoiceAsync(db, amount: 100m, currencyId: EurId, fxRate: 1.10m);

        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.BookId.Should().Be(BookId);
        item.CustomerId.Should().Be(invoice.CustomerId);
        item.SourceType.Should().Be("Invoice");
        item.SourceId.Should().Be(invoice.Id);
        item.DocumentNumber.Should().Be(invoice.InvoiceNumber);
        item.DocumentDate.Should().Be(invoice.InvoiceDate);
        item.DueDate.Should().Be(invoice.DueDate);
        item.CurrencyId.Should().Be(EurId);
        item.FxRate.Should().Be(1.10m);
        item.OriginalTxnAmount.Should().Be(100m);
        item.OriginalFunctionalAmount.Should().Be(110m); // txn × booking rate, posting-rounded
        item.AppliedTxnAmount.Should().Be(0m);
        item.AppliedFunctionalAmount.Should().Be(0m);
        item.Status.Should().Be(OpenItemStatus.Open);
        item.OpenTxnAmount.Should().Be(100m);
        item.OpenFunctionalAmount.Should().Be(110m);
    }

    [Fact]
    public async Task ArItem_IncludesTax_MatchesPostedControlTotal()
    {
        using var db = await SeedAsync();
        // 200 + 8% tax → the Dr AR_CONTROL is 216; the item must carry the POSTED total.
        var invoice = await AddInvoiceAsync(db, amount: 200m, taxRate: 0.08m);

        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.OriginalTxnAmount.Should().Be(216m);
        item.OriginalFunctionalAmount.Should().Be(216m);

        // …and equals the AR_CONTROL debit exactly (the lock-step invariant).
        var arLine = await db.JournalLines.SingleAsync(l => l.GlAccountId == ArControlId);
        arLine.FunctionalAmount.Should().Be(item.OriginalFunctionalAmount);
    }

    [Fact]
    public async Task ArItem_RepostedInvoice_NoDuplicate()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, amount: 100m);
        var service = InvoiceService(db);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        (await db.ArOpenItems.CountAsync()).Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ArItem_FullGlOff_NoneCreated()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, amount: 100m);

        await InvoiceService(db, fullGlOn: false).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        (await db.ArOpenItems.AnyAsync()).Should().BeFalse();
    }

    // ─────────────────────────── AR — applications + status transitions ───────────────────────────

    [Fact]
    public async Task ArItem_PartialThenExactClose_TransitionsStatuses()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, amount: 200m);
        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        // Partial: 80 of 200 applied.
        var p1 = await AddPaymentAsync(db, invoice, appliedForeign: 80m);
        await PaymentService(db).PostPaymentCreatedAsync(p1.Id, createdByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(80m);
        item.AppliedFunctionalAmount.Should().Be(80m);
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);
        item.OpenTxnAmount.Should().Be(120m);

        // Exact close: the remaining 120.
        var p2 = await AddPaymentAsync(db, invoice, appliedForeign: 120m);
        await PaymentService(db).PostPaymentCreatedAsync(p2.Id, createdByUserId: 7);

        item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(200m);
        item.AppliedFunctionalAmount.Should().Be(200m);
        item.Status.Should().Be(OpenItemStatus.Closed);
        item.OpenTxnAmount.Should().Be(0m);
        item.OpenFunctionalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ArItem_FxApplication_ReliefAtBookingRate_NotSettlement()
    {
        using var db = await SeedAsync();
        // EUR 100 booked @1.10 (carrying 110); settled @1.05 (cash 105). The item must move by the
        // BOOKING-rate relief (110) — the FX plug belongs to the payment entry, not the item.
        var invoice = await AddInvoiceAsync(db, amount: 100m, currencyId: EurId, fxRate: 1.10m);
        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var payment = await AddPaymentAsync(db, invoice, appliedForeign: 100m, settlementFxRate: 1.05m);
        await PaymentService(db).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(100m);
        item.AppliedFunctionalAmount.Should().Be(110m); // booking rate, not 105
        item.Status.Should().Be(OpenItemStatus.Closed);
        item.OpenFunctionalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ArItem_RepostedPayment_DoesNotDoubleApply()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, amount: 200m);
        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var payment = await AddPaymentAsync(db, invoice, appliedForeign: 80m);
        var service = PaymentService(db);
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(80m); // not 160 — the journal de-dupe guard covers the item too
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);
    }

    [Fact]
    public async Task ArItem_LegacyInvoiceWithoutItem_PaymentSkipsWithoutCreating()
    {
        using var db = await SeedAsync();
        // Invoice exists operationally but was never AR-posted (legacy / pre-open-item document) —
        // no item. The payment posting must log-and-skip, NOT create-then-apply (backfill owns legacy).
        var invoice = await AddInvoiceAsync(db, amount: 100m);

        var payment = await AddPaymentAsync(db, invoice, appliedForeign: 100m);
        await PaymentService(db).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        (await db.ArOpenItems.AnyAsync()).Should().BeFalse();
        // The journal itself still posts (cash + AR relief) — only the item maintenance skips.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ─────────────────────────── AR — void restores ───────────────────────────

    [Fact]
    public async Task ArItem_PaymentVoid_RestoresAppliedAndStatus()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db, amount: 100m, currencyId: EurId, fxRate: 1.10m);
        await InvoiceService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var payment = await AddPaymentAsync(db, invoice, appliedForeign: 100m, settlementFxRate: 1.05m);
        var service = PaymentService(db);
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        (await db.ArOpenItems.SingleAsync()).Status.Should().Be(OpenItemStatus.Closed);

        await service.ReversePaymentCreatedAsync(payment.Id, "duplicate entry", reversedByUserId: 7);

        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(0m);
        item.AppliedFunctionalAmount.Should().Be(0m); // booking-rate decrement mirrors the increment
        item.Status.Should().Be(OpenItemStatus.Open);
        item.OpenFunctionalAmount.Should().Be(110m);

        // A second reversal attempt is a no-op (origination already reversed) — no negative drift.
        await service.ReversePaymentCreatedAsync(payment.Id, "again", reversedByUserId: 7);
        (await db.ArOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(0m);
    }

    // ─────────────────────────── AP — mirrors ───────────────────────────

    [Fact]
    public async Task ApItem_CreatedOnBillPosting_FunctionalAtBookingRate()
    {
        using var db = await SeedAsync();
        // EUR bill, foreign 100 booked @1.10 → functional 110.
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, amount: 100m, currencyId: EurId, fxRate: 1.10m);

        await BillService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.BookId.Should().Be(BookId);
        item.VendorId.Should().Be(vendorId);
        item.SourceType.Should().Be("VendorBill");
        item.SourceId.Should().Be(bill.Id);
        item.DocumentNumber.Should().Be(bill.BillNumber);
        item.DueDate.Should().Be(bill.DueDate);
        item.CurrencyId.Should().Be(EurId);
        item.FxRate.Should().Be(1.10m);
        item.OriginalTxnAmount.Should().Be(100m);
        item.OriginalFunctionalAmount.Should().Be(110m);
        item.Status.Should().Be(OpenItemStatus.Open);
    }

    [Fact]
    public async Task ApItem_RepostedBill_NoDuplicate_AndFullGlOffNone()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);

        // FULLGL off → no item.
        var darkBill = await AddBillAsync(db, vendorId, amount: 50m);
        await BillService(db, fullGlOn: false).PostVendorBillApprovedAsync(darkBill.Id, approvedByUserId: 7);
        (await db.ApOpenItems.AnyAsync()).Should().BeFalse();

        // Re-post → one item, one journal.
        var bill = await AddBillAsync(db, vendorId, amount: 100m);
        var service = BillService(db);
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        (await db.ApOpenItems.CountAsync()).Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApItem_VendorPayment_PartialThenClose_AtBookingRate()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, amount: 100m, currencyId: EurId, fxRate: 1.10m);
        await BillService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        // Partial 40 (foreign) @ settlement 1.05 → item moves by booking-rate relief 44.
        var p1 = await AddVendorPaymentAsync(db, bill, appliedForeign: 40m, settlementFxRate: 1.05m);
        await VendorPaymentService(db).PostVendorPaymentCreatedAsync(p1.Id, createdByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(40m);
        item.AppliedFunctionalAmount.Should().Be(44m); // 40 × 1.10 booking — not 42 settlement
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);

        // Exact close with the remaining 60.
        var p2 = await AddVendorPaymentAsync(db, bill, appliedForeign: 60m, settlementFxRate: 1.05m);
        await VendorPaymentService(db).PostVendorPaymentCreatedAsync(p2.Id, createdByUserId: 7);

        item = await db.ApOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(100m);
        item.AppliedFunctionalAmount.Should().Be(110m);
        item.Status.Should().Be(OpenItemStatus.Closed);
        item.OpenFunctionalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ApItem_VendorPaymentVoid_RestoresAppliedAndStatus()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, amount: 100m);
        await BillService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var payment = await AddVendorPaymentAsync(db, bill, appliedForeign: 100m);
        var service = VendorPaymentService(db);
        await service.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        (await db.ApOpenItems.SingleAsync()).Status.Should().Be(OpenItemStatus.Closed);

        await service.ReverseVendorPaymentCreatedAsync(payment.Id, "wrong vendor", reversedByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(0m);
        item.AppliedFunctionalAmount.Should().Be(0m);
        item.Status.Should().Be(OpenItemStatus.Open);
    }

    [Fact]
    public async Task ApItem_BillVoid_FlipsToVoided()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, amount: 100m);
        var service = BillService(db);
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        await service.ReverseVendorBillApprovedAsync(bill.Id, reversedByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.Status.Should().Be(OpenItemStatus.Voided);
        // Voided is terminal — RecomputeStatus never resurrects it.
        item.RecomputeStatus();
        item.Status.Should().Be(OpenItemStatus.Voided);
    }
}
