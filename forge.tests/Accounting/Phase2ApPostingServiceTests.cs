using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE A — AP posting services (the AP counterpart of the Phase-1 AR service tests). Proves,
/// on the InMemory provider (posting LOGIC; the transaction-atomicity proof is a separate Postgres test):
///   • DARK by default — a no-op while CAP-ACCT-FULLGL is OFF (zero behavior change);
///   • VendorBill approved → Dr line accounts (+ purchase tax) / Cr AP_CONTROL (party = vendor);
///   • VendorPayment → Dr AP_CONTROL (applied, party = vendor) / Cr CASH; overpayment → Dr PREPAID_EXPENSE;
///   • idempotent — a re-post returns the existing entry (no duplicate).
/// </summary>
public class Phase2ApPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2; // 2nd currency for the multi-currency FX coverage
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ApControlId = 200;
    private const int OperatingExpenseId = 201;
    private const int CashId = 202;
    private const int PrepaidExpenseId = 203;
    private const int GrniId = 210;   // STAGE D — 3-way match
    private const int PpvId = 211;    // STAGE D — purchase price variance
    private const int FxGainId = 212; // Phase-4 — realized FX gain
    private const int FxLossId = 213; // Phase-4 — realized FX loss
    private const int CashInTransitId = 214; // §7 BANK-002 — electronic-disbursement clearing

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
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static VendorBillApPostingService BillService(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static VendorPaymentCashPostingService PaymentService(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

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

        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = OperatingExpenseId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PrepaidExpenseId, BookId = BookId, AccountNumber = "12000", Name = "Prepaid Expenses", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PpvId, BookId = BookId, AccountNumber = "51000", Name = "Purchase Price Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CashInTransitId, BookId = BookId, AccountNumber = "10150", Name = "Cash in Transit", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "PREPAID_EXPENSE", GlAccountId = PrepaidExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId },
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_PRICE_VARIANCE", GlAccountId = PpvId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH_IN_TRANSIT", GlAccountId = CashInTransitId });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<int> AddVendorAsync(AppDbContext db, string name = "Delta Supply")
    {
        var vendor = new Vendor { CompanyName = name, IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<VendorBill> AddBillAsync(
        AppDbContext db, int vendorId, decimal taxAmount = 0m,
        VendorBillStatus status = VendorBillStatus.Approved,
        int currencyId = UsdId, decimal fxRate = 1m)
    {
        var bill = new VendorBill
        {
            BillNumber = "BILL-1001",
            VendorId = vendorId,
            VendorInvoiceNumber = "V-555",
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = status,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            TaxAmount = taxAmount,
            Lines =
            [
                new VendorBillLine { Description = "Steel bar", Quantity = 2, UnitPrice = 50m, LineNumber = 1, AccountDeterminationKey = "OPERATING_EXPENSE" },
                new VendorBillLine { Description = "Fasteners", Quantity = 1, UnitPrice = 100m, LineNumber = 2, AccountDeterminationKey = "OPERATING_EXPENSE" },
            ],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    private static async Task<VendorPayment> AddPaymentAsync(
        AppDbContext db, int vendorId, decimal amount, int? billId = null, decimal? appliedAmount = null,
        decimal settlementFxRate = 1m, PaymentMethod method = PaymentMethod.Check)
    {
        var payment = new VendorPayment
        {
            PaymentNumber = "VPMT-1001",
            VendorId = vendorId,
            Method = method,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero),
        };
        if (billId is int bid && appliedAmount is decimal applied)
            payment.Applications.Add(new VendorPaymentApplication { VendorBillId = bid, Amount = applied, SettlementFxRate = settlementFxRate });

        db.Set<VendorPayment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    // ─────────────────────────── VendorBill ───────────────────────────

    [Fact]
    public async Task Bill_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);

        await BillService(db, fullGlOn: false).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.LedgerBalances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Bill_WhenFullGlOn_PostsExpenseAndAp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AP);
        entry.SourceType.Should().Be("VendorBill");
        entry.SourceId.Should().Be(bill.Id);
        entry.EntryDate.Should().Be(new DateOnly(2026, 1, 18));
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedBy.Should().Be(7);

        // Dr G&A for the two lines (100 + 100 = 200).
        entry.Lines.Where(l => l.GlAccountId == OperatingExpenseId).Sum(l => l.Debit).Should().Be(200m);

        // Cr AP for the total, party = vendor.
        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.Credit.Should().Be(200m);
        ap.SubledgerPartyType.Should().Be(SubledgerPartyType.Vendor);
        ap.SubledgerPartyId.Should().Be(vendorId);

        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Bill_ForeignCurrency_PostsApAtBookingRate()
    {
        // Stage 2 — a EUR bill at booking rate 1.10. The AP/expense entry posts in the TRANSACTION currency
        // (EUR): each line's TxnAmount is the foreign amount, and FunctionalAmount = foreign × 1.10.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, currencyId: EurId, fxRate: 1.10m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.CurrencyId.Should().Be(EurId);

        // AP line: TxnAmount 200 EUR; FunctionalAmount 200 × 1.10 = 220.
        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.CurrencyId.Should().Be(EurId);
        ap.Credit.Should().Be(200m);
        ap.TxnAmount.Should().Be(200m);
        ap.FxRate.Should().Be(1.10m);
        ap.FunctionalAmount.Should().Be(220m);

        // Expense (G&A) lines: foreign 200, functional 220.
        entry.Lines.Where(l => l.GlAccountId == OperatingExpenseId).Sum(l => l.TxnAmount).Should().Be(200m);
        entry.Lines.Where(l => l.GlAccountId == OperatingExpenseId).Sum(l => l.FunctionalAmount).Should().Be(220m);

        // Balanced in both dimensions.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
        entry.Lines.Where(l => l.Debit > 0).Sum(l => l.FunctionalAmount)
            .Should().Be(entry.Lines.Where(l => l.Credit > 0).Sum(l => l.FunctionalAmount));
    }

    [Fact]
    public async Task Bill_WithTax_AddsPurchaseTaxToExpenseAndAp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, taxAmount: 16m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        // Lines 200 + tax 16 expensed to G&A = 216.
        entry.Lines.Where(l => l.GlAccountId == OperatingExpenseId).Sum(l => l.Debit).Should().Be(216m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(216m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Bill_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var service = BillService(db, fullGlOn: true);

        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ─────────────────── VendorBill — 3-way match / PPV (STAGE D) ───────────────────

    /// <summary>
    /// Creates a PO with one line (received <paramref name="receivedQty"/>, already-billed
    /// <paramref name="alreadyBilledQty"/>) at <paramref name="poUnitPrice"/>, plus a PO-matched bill that
    /// bills <paramref name="billQty"/> at <paramref name="billUnitPrice"/>. Returns the bill.
    /// </summary>
    private static async Task<VendorBill> AddPoMatchedBillAsync(
        AppDbContext db, int vendorId,
        decimal poUnitPrice, decimal receivedQty, decimal billQty, decimal billUnitPrice,
        decimal alreadyBilledQty = 0m, decimal taxAmount = 0m,
        VendorBillStatus status = VendorBillStatus.Approved)
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Part>().Add(part);

        var po = new PurchaseOrder { PONumber = "PO-7", VendorId = vendorId, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();

        var poLine = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id,
            OrderedQuantity = receivedQty, ReceivedQuantity = receivedQty,
            BilledQuantity = alreadyBilledQty, UnitPrice = poUnitPrice,
        };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();

        var bill = new VendorBill
        {
            BillNumber = "BILL-2001",
            VendorId = vendorId,
            VendorInvoiceNumber = "V-777",
            PurchaseOrderId = po.Id,
            Status = status,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            TaxAmount = taxAmount,
            Lines =
            [
                new VendorBillLine
                {
                    Description = "PO line", Quantity = billQty, UnitPrice = billUnitPrice,
                    LineNumber = 1, PurchaseOrderLineId = poLine.Id, AccountDeterminationKey = "OPERATING_EXPENSE",
                },
            ],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    [Fact]
    public async Task PoMatchedBill_ExactPrice_ClearsGrniNoVariance()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // PO price 10, received 5, bill 5 @ 10 → Dr GRNI 50 / Cr AP 50; no PPV, no expense debit.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 5m, billUnitPrice: 10m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == GrniId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(50m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == PpvId);
        entry.Lines.Should().NotContain(l => l.GlAccountId == OperatingExpenseId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_BilledAbovePoPrice_DebitsUnfavorablePpv()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // PO 10, bill 12, qty 5 → grniClear 50, ppv +10 (Dr), AP 60.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 5m, billUnitPrice: 12m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == GrniId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == PpvId).Debit.Should().Be(10m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(60m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_BilledBelowPoPrice_CreditsFavorablePpv()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // PO 10, bill 8, qty 5 → grniClear 50, ppv -10 (Cr), AP 40.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 5m, billUnitPrice: 8m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == GrniId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == PpvId).Credit.Should().Be(10m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(40m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_MixedZeroPricedAndPricedLines_ClearsAllGrni()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);

        // One PO, two received lines: one billed at $0 (free replacement), one at PO price. The free line
        // must STILL clear its GRNI (Dr GRNI at PO price / Cr PPV favorable) — a regression guard for the
        // zero-priced-line skip bug (skipping it would strand the GRNI credit while BilledQuantity advances).
        var partA = new Part { PartNumber = $"P-A-{Guid.NewGuid():N}", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        var partB = new Part { PartNumber = $"P-B-{Guid.NewGuid():N}", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.Set<Part>().AddRange(partA, partB);
        var po = new PurchaseOrder { PONumber = "PO-MIX", VendorId = vendorId, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var lineA = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = partA.Id, OrderedQuantity = 5m, ReceivedQuantity = 5m, UnitPrice = 10m };
        var lineB = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = partB.Id, OrderedQuantity = 2m, ReceivedQuantity = 2m, UnitPrice = 20m };
        db.Set<PurchaseOrderLine>().AddRange(lineA, lineB);
        await db.SaveChangesAsync();

        var bill = new VendorBill
        {
            BillNumber = "BILL-MIX", VendorId = vendorId, PurchaseOrderId = po.Id,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            Lines =
            [
                new VendorBillLine { Description = "Free replacement", Quantity = 5m, UnitPrice = 0m, LineNumber = 1, PurchaseOrderLineId = lineA.Id, AccountDeterminationKey = "OPERATING_EXPENSE" },
                new VendorBillLine { Description = "Priced", Quantity = 2m, UnitPrice = 20m, LineNumber = 2, PurchaseOrderLineId = lineB.Id, AccountDeterminationKey = "OPERATING_EXPENSE" },
            ],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Where(l => l.GlAccountId == GrniId).Sum(l => l.Debit).Should().Be(90m); // 5×10 + 2×20, one line each
        entry.Lines.Single(l => l.GlAccountId == PpvId).Credit.Should().Be(50m);   // favorable on the free line
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(40m); // billed total
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_WithTax_AddsTaxToExpenseAndAp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // PO 10 = bill 10, qty 5 → grniClear 50; + tax 4 → Dr GRNI 50 / Dr OPEX 4 / Cr AP 54.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 5m, billUnitPrice: 10m, taxAmount: 4m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == GrniId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == OperatingExpenseId).Debit.Should().Be(4m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(54m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == PpvId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_OverBill_ThrowsGrniInsufficient()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // received 5, bill 6 → over-bill: can't clear GRNI never accrued.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 6m, billUnitPrice: 10m);

        var act = () => BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("GRNI_INSUFFICIENT");
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PoMatchedBill_WithinUnbilledRemainder_ClearsAtPoPrice()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // received 5, already billed 3 → 2 unbilled-received; bill 2 @ PO price 10 → grniClear 20.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 2m, billUnitPrice: 10m, alreadyBilledQty: 3m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == GrniId).Debit.Should().Be(20m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(20m);
    }

    [Fact]
    public async Task PoMatchedBill_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 10m, receivedQty: 5m, billQty: 5m, billUnitPrice: 10m);
        var service = BillService(db, fullGlOn: true);

        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PoMatchedBill_TwoLinesSamePoLine_AccumulatesPpv()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);

        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.Set<Part>().Add(part);
        var po = new PurchaseOrder { PONumber = "PO-2L", VendorId = vendorId, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var poLine = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 5m, ReceivedQuantity = 5m, UnitPrice = 10m };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();

        // Two bill lines on the SAME PO line: 2 @ 12 (unfavorable +4) and 3 @ 8 (favorable −6) → netPpv −2 (Cr).
        var bill = new VendorBill
        {
            BillNumber = "BILL-2L", VendorId = vendorId, PurchaseOrderId = po.Id, Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            Lines =
            [
                new VendorBillLine { Description = "L1", Quantity = 2m, UnitPrice = 12m, LineNumber = 1, PurchaseOrderLineId = poLine.Id, AccountDeterminationKey = "OPERATING_EXPENSE" },
                new VendorBillLine { Description = "L2", Quantity = 3m, UnitPrice = 8m, LineNumber = 2, PurchaseOrderLineId = poLine.Id, AccountDeterminationKey = "OPERATING_EXPENSE" },
            ],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Where(l => l.GlAccountId == GrniId).Sum(l => l.Debit).Should().Be(50m); // 2×10 + 3×10
        entry.Lines.Single(l => l.GlAccountId == PpvId).Credit.Should().Be(2m);             // net favorable 2
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(48m);      // 24 + 24
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PoMatchedBill_ZeroPoPrice_BillsAsFullUnfavorablePpv()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // PO price 0 (nothing accrued at receipt), billed 2 @ 5 → no GRNI line, Dr PPV 10, Cr AP 10.
        var bill = await AddPoMatchedBillAsync(db, vendorId, poUnitPrice: 0m, receivedQty: 2m, billQty: 2m, billUnitPrice: 5m);

        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Should().NotContain(l => l.GlAccountId == GrniId);
        entry.Lines.Single(l => l.GlAccountId == PpvId).Debit.Should().Be(10m);
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Credit.Should().Be(10m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    // ─────────────────────────── VendorPayment ───────────────────────────

    [Fact]
    public async Task Payment_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var payment = await AddPaymentAsync(db, vendorId, amount: 100m, billId: bill.Id, appliedAmount: 100m);

        await PaymentService(db, fullGlOn: false).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Payment_FullyApplied_PostsApAndCash()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var payment = await AddPaymentAsync(db, vendorId, amount: 100m, billId: bill.Id, appliedAmount: 100m);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AP);
        entry.SourceType.Should().Be("VendorPayment");

        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.Debit.Should().Be(100m);
        ap.SubledgerPartyType.Should().Be(SubledgerPartyType.Vendor);
        ap.SubledgerPartyId.Should().Be(vendorId);

        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(100m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == PrepaidExpenseId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_Overpayment_BooksUnappliedToVendorAdvance()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        // Pays 150, applies 100 to the bill → 50 unapplied (advance).
        var payment = await AddPaymentAsync(db, vendorId, amount: 150m, billId: bill.Id, appliedAmount: 100m);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == PrepaidExpenseId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(150m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    // ─────────────────── VendorPayment — cash-in-transit clearing (§7 BANK-002) ───────────────────

    [Fact]
    public async Task Payment_Electronic_CreditsCashInTransit_NotCash()
    {
        // BankTransfer = electronic → origination only records the INTENT to move money: the cash credit
        // goes to CASH_IN_TRANSIT; CASH is untouched until the bank transmission settles.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var payment = await AddPaymentAsync(
            db, vendorId, amount: 100m, billId: bill.Id, appliedAmount: 100m, method: PaymentMethod.BankTransfer);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == CashInTransitId).Credit.Should().Be(100m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == CashId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_Wire_Overpayment_AdvanceAndCitBothPost()
    {
        // Wire is electronic too: Dr AP 100 / Dr PREPAID 50 / Cr CASH_IN_TRANSIT 150.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var payment = await AddPaymentAsync(
            db, vendorId, amount: 150m, billId: bill.Id, appliedAmount: 100m, method: PaymentMethod.Wire);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == PrepaidExpenseId).Debit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == CashInTransitId).Credit.Should().Be(150m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == CashId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_Electronic_RealizedFxGain_BalancesWithCit()
    {
        // EUR bill foreign 100 booked @1.10 → AP carrying 110; settled @1.05 → cash 105, electronically.
        // Dr AP 110 / Cr CASH_IN_TRANSIT 105 / Cr FX_GAIN 5 — the FX plug is unchanged by the CIT routing.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, currencyId: EurId, fxRate: 1.10m);
        var payment = await AddPaymentAsync(
            db, vendorId, amount: 105m, billId: bill.Id, appliedAmount: 100m, settlementFxRate: 1.05m,
            method: PaymentMethod.BankTransfer);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(110m);
        entry.Lines.Single(l => l.GlAccountId == CashInTransitId).Credit.Should().Be(105m);
        entry.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(5m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == CashId);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    // ─────────────────────── VendorPayment — realized FX (Phase-4) ───────────────────────

    [Fact]
    public async Task Payment_RealizedFxGain_CreditsFxGain_ApNetsToZero_Balances()
    {
        // EUR bill foreign 100 booked @1.10 → AP carrying 110. Settled @1.05 → cash 105 (functional).
        // Plug +5 → Cr FX_GAIN (settled a bigger payable for less cash). Dr AP 110 / Cr Cash 105 / Cr FX_GAIN 5.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, currencyId: EurId, fxRate: 1.10m);
        var payment = await AddPaymentAsync(db, vendorId, amount: 105m, billId: bill.Id, appliedAmount: 100m, settlementFxRate: 1.05m);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();

        // AP relieved at the booked carrying value (110) → fully nets the bill's AP to zero.
        var ap = entry.Lines.Single(l => l.GlAccountId == ApControlId);
        ap.Debit.Should().Be(110m);
        ap.SubledgerPartyType.Should().Be(SubledgerPartyType.Vendor);
        ap.SubledgerPartyId.Should().Be(vendorId);

        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(105m);
        entry.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(5m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxLossId);
        entry.Lines.Should().OnlyContain(l => l.FxRate == 1m); // fully functional settlement entry

        // BALANCES: Dr 110 == Cr 105 + 5.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_RealizedFxLoss_DebitsFxLoss_ApNetsToZero_Balances()
    {
        // EUR bill foreign 100 booked @1.10 → AP carrying 110. Settled @1.15 → cash 115 (functional).
        // Plug −5 → Dr FX_LOSS (paid more cash than the payable relieved). Dr AP 110 / Dr FX_LOSS 5 / Cr Cash 115.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId, currencyId: EurId, fxRate: 1.10m);
        var payment = await AddPaymentAsync(db, vendorId, amount: 115m, billId: bill.Id, appliedAmount: 100m, settlementFxRate: 1.15m);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(110m);
        entry.Lines.Single(l => l.GlAccountId == FxLossId).Debit.Should().Be(5m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(115m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxGainId);

        // BALANCES: Dr 110 + 5 == Cr 115.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_FunctionalCurrencyBill_NoFxLine_BackwardCompatible()
    {
        // USD (functional) bill booked @1, settled @1 → apRelief == cash == applied → NO FX line.
        // Same shape as the pre-FX single-currency path: Dr AP 100 / Cr Cash 100.
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId); // USD @1 (defaults)
        var payment = await AddPaymentAsync(db, vendorId, amount: 100m, billId: bill.Id, appliedAmount: 100m);

        await PaymentService(db, fullGlOn: true).PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ApControlId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(100m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxGainId || l.GlAccountId == FxLossId);
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Payment_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId);
        var payment = await AddPaymentAsync(db, vendorId, amount: 100m, billId: bill.Id, appliedAmount: 100m);
        var service = PaymentService(db, fullGlOn: true);

        await service.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        await service.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ─────────────────────── AP-001 — open-item sub-ledger maintenance ───────────────────────

    [Fact]
    public async Task Bill_CreatesApOpenItem_ForThePostedControlTotal()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // 200 lines + 16 tax → Cr AP_CONTROL 216; the open item must carry the POSTED total.
        var bill = await AddBillAsync(db, vendorId, taxAmount: 16m);
        var service = BillService(db, fullGlOn: true);

        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.SourceType.Should().Be("VendorBill");
        item.SourceId.Should().Be(bill.Id);
        item.VendorId.Should().Be(vendorId);
        item.OriginalTxnAmount.Should().Be(216m);
        item.OriginalFunctionalAmount.Should().Be(216m);
        item.AppliedTxnAmount.Should().Be(0m);
        item.Status.Should().Be(OpenItemStatus.Open);

        // Idempotent: a re-approve creates no duplicate item.
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        (await db.ApOpenItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Payment_IncrementsApOpenItem_AndRepostNeverDoubleApplies()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var bill = await AddBillAsync(db, vendorId); // 200 total (no tax)
        await BillService(db, fullGlOn: true).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var payment = await AddPaymentAsync(db, vendorId, amount: 80m, billId: bill.Id, appliedAmount: 80m);
        var service = PaymentService(db, fullGlOn: true);
        await service.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var item = await db.ApOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(80m);
        item.AppliedFunctionalAmount.Should().Be(80m);
        item.Status.Should().Be(OpenItemStatus.PartiallyApplied);
        item.OpenTxnAmount.Should().Be(120m);

        // A re-post never double-applies (the journal de-dupe guard covers the item maintenance too).
        await service.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        (await db.ApOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(80m);
    }
}
