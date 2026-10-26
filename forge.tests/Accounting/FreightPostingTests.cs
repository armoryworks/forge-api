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
/// Freight postings: AR freight lines credit FREIGHT_INCOME (not SALES_REVENUE), deferred freight reclasses to
/// FREIGHT_INCOME on delivery, and AP freight bills clear the receipt's FREIGHT_CLEARING accrual for their PO with
/// any excess to FREIGHT_IN; closing a PO releases what no bill cleared.
/// </summary>
public class FreightPostingTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ArControlId = 101;
    private const int RevenueId = 102;
    private const int FreightIncomeId = 103;
    private const int SalesTaxPayableId = 104;
    private const int DeferredRevenueId = 105;
    private const int ApControlId = 201;
    private const int FreightClearingId = 202;
    private const int FreightInId = 203;
    private const int FreightOutId = 204;
    private const int OperatingExpenseId = 205;
    private const int GrniId = 206;
    private const int PpvId = 207;

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

    private static InvoiceArPostingService ArService(AppDbContext db) => new(db, Engine(db), new FakeCapabilities(true));

    private static VendorBillApPostingService ApService(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal,
        ControlAccountType? control = null)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = normal,
            IsControlAccount = control is not null, ControlType = control, IsPostable = true, IsActive = true,
        };

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
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
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            Account(ArControlId, "11000", "Accounts Receivable", AccountType.Asset, NormalBalance.Debit, ControlAccountType.AR),
            Account(RevenueId, "40000", "Sales Revenue", AccountType.Income, NormalBalance.Credit),
            Account(FreightIncomeId, "40500", "Freight Income", AccountType.Income, NormalBalance.Credit),
            Account(SalesTaxPayableId, "23000", "Sales Tax Payable", AccountType.Liability, NormalBalance.Credit),
            Account(DeferredRevenueId, "24000", "Deferred Revenue", AccountType.Liability, NormalBalance.Credit),
            Account(ApControlId, "20000", "Accounts Payable", AccountType.Liability, NormalBalance.Credit, ControlAccountType.AP),
            Account(FreightClearingId, "22000", "Freight Clearing", AccountType.Liability, NormalBalance.Credit),
            Account(FreightInId, "50100", "Freight-In", AccountType.Expense, NormalBalance.Debit),
            Account(FreightOutId, "61200", "Freight-Out (Delivery Expense)", AccountType.Expense, NormalBalance.Debit),
            Account(OperatingExpenseId, "60000", "General & Administrative", AccountType.Expense, NormalBalance.Debit),
            Account(GrniId, "21000", "GRNI", AccountType.Liability, NormalBalance.Credit),
            Account(PpvId, "51000", "Purchase Price Variance", AccountType.Expense, NormalBalance.Debit));

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_INCOME", GlAccountId = FreightIncomeId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = SalesTaxPayableId },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = DeferredRevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_CLEARING", GlAccountId = FreightClearingId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_IN", GlAccountId = FreightInId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_OUT", GlAccountId = FreightOutId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OperatingExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId },
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_PRICE_VARIANCE", GlAccountId = PpvId });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Invoice> AddInvoiceAsync(AppDbContext db, int? shipmentId = null)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-5001",
            CustomerId = customer.Id,
            CurrencyId = UsdId,
            ShipmentId = shipmentId,
            InvoiceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            TaxRate = 0.0725m,
            Lines =
            [
                new InvoiceLine { Description = "Molded housings", Quantity = 1, UnitPrice = 9_685m, LineNumber = 1 },
                new InvoiceLine
                {
                    Description = "Freight", Quantity = 1, UnitPrice = 300m, LineNumber = 2,
                    LineType = DocumentLineType.Freight, TaxCode = InvoiceLine.NonTaxableCode,
                },
            ],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static decimal Credit(JournalEntry entry, int accountId) => entry.Lines.Where(l => l.GlAccountId == accountId).Sum(l => l.Credit);
    private static decimal Debit(JournalEntry entry, int accountId) => entry.Lines.Where(l => l.GlAccountId == accountId).Sum(l => l.Debit);

    [Fact]
    public async Task Finalize_FreightLine_CreditsFreightIncome_AndTaxesGoodsOnly()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db);

        await ArService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Credit(entry, RevenueId).Should().Be(9_685m);
        Credit(entry, FreightIncomeId).Should().Be(300m);
        Credit(entry, SalesTaxPayableId).Should().Be(702.16m);
        Debit(entry, ArControlId).Should().Be(10_687.16m);
    }

    [Fact]
    public async Task Finalize_TaxableFreight_IsInTheTaxBase()
    {
        using var db = await SeedAsync();
        var invoice = await AddInvoiceAsync(db);
        invoice.Lines.Single(l => l.IsFreight).TaxCode = "TAX";
        await db.SaveChangesAsync();

        await ArService(db).PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Credit(entry, SalesTaxPayableId).Should().Be(723.91m);
        Credit(entry, FreightIncomeId).Should().Be(300m);
    }

    [Fact]
    public async Task DeliveryReclass_SplitsDeferredRevenueBetweenSalesAndFreightIncome()
    {
        using var db = await SeedAsync();
        var shipment = new Shipment { Id = 600, ShipmentNumber = "SHP-1", SalesOrderId = 1, Status = ShipmentStatus.InTransit };
        db.Set<Shipment>().Add(shipment);
        await db.SaveChangesAsync();
        var invoice = await AddInvoiceAsync(db, shipment.Id);
        var service = ArService(db);

        await service.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 7);
        var original = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Credit(original, DeferredRevenueId).Should().Be(9_985m);
        Credit(original, FreightIncomeId).Should().Be(0m);

        shipment.Status = ShipmentStatus.Delivered;
        shipment.DeliveredDate = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        await service.PostDeliveryReclassAsync(invoice.Id, deliveredByUserId: 8);

        var reclass = await db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"{JournalSource.AR}:Invoice:{invoice.Id}:REVENUE_RECLASS");
        Debit(reclass, DeferredRevenueId).Should().Be(9_985m);
        Credit(reclass, RevenueId).Should().Be(9_685m);
        Credit(reclass, FreightIncomeId).Should().Be(300m);
    }

    [Fact]
    public void BuildReclassLines_PutsAResidualOnTheLargestLine()
    {
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-1",
            Lines =
            [
                new InvoiceLine { Description = "A", Quantity = 1, UnitPrice = 100m, LineNumber = 1 },
                new InvoiceLine { Description = "Freight", Quantity = 1, UnitPrice = 20m, LineNumber = 2, LineType = DocumentLineType.Freight },
            ],
        };

        var lines = InvoiceArPostingService.BuildReclassLines(invoice, deferredCredit: 120.01m);

        lines.Where(l => l.AccountKey == "DEFERRED_REVENUE").Sum(l => l.Debit).Should().Be(120.01m);
        lines.Single(l => l.AccountKey == "SALES_REVENUE").Credit.Should().Be(100.01m);
        lines.Single(l => l.AccountKey == "FREIGHT_INCOME").Credit.Should().Be(20m);
    }

    private static async Task<(int VendorId, int PoId)> AddPoWithFreightReceiptAsync(AppDbContext db, decimal freight)
    {
        var vendor = new Vendor { CompanyName = "Delta Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-77", VendorId = vendor.Id, Status = PurchaseOrderStatus.Received };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();

        await Engine(db).PostAsSystemAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = new DateOnly(2026, 1, 10),
            Source = JournalSource.Inventory,
            SourceType = "Receipt",
            SourceId = po.Id,
            CurrencyId = UsdId,
            IdempotencyKey = $"Inventory:Receipt:{po.Id}:RCV-1",
            Lines =
            [
                new PostingLine { AccountKey = "OPERATING_EXPENSE", Debit = freight, Description = "landed freight" },
                new PostingLine { AccountKey = "FREIGHT_CLEARING", Credit = freight, Description = "Freight clearing — receipt RCV-1" },
            ],
        }, 7);
        return (vendor.Id, po.Id);
    }

    private static async Task<VendorBill> AddFreightBillAsync(
        AppDbContext db, int vendorId, decimal amount, int? poId, string number, string key = "FREIGHT_IN")
    {
        var bill = new VendorBill
        {
            BillNumber = number,
            VendorId = vendorId,
            CurrencyId = UsdId,
            Status = VendorBillStatus.Approved,
            BillDate = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero),
            Lines =
            [
                new VendorBillLine
                {
                    Description = "Carrier freight", Quantity = 1, UnitPrice = amount, LineNumber = 1,
                    LineType = DocumentLineType.Freight, FreightForPurchaseOrderId = poId, AccountDeterminationKey = key,
                },
            ],
        };
        db.Set<VendorBill>().Add(bill);
        await db.SaveChangesAsync();
        return bill;
    }

    private static Task<JournalEntry> BillEntryAsync(AppDbContext db, int billId)
        => db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"{JournalSource.AP}:VendorBill:{billId}:BILL");

    [Fact]
    public async Task CarrierBill_AbovePoAccrual_ClearsAccrualAndSendsExcessToFreightIn()
    {
        using var db = await SeedAsync();
        var (vendorId, poId) = await AddPoWithFreightReceiptAsync(db, 50m);
        var bill = await AddFreightBillAsync(db, vendorId, 55m, poId, "BILL-1");

        await ApService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await BillEntryAsync(db, bill.Id);
        Debit(entry, FreightClearingId).Should().Be(50m);
        Debit(entry, FreightInId).Should().Be(5m);
        Credit(entry, ApControlId).Should().Be(55m);
        (await db.Set<VendorBillLine>().SingleAsync()).FreightClearedAmount.Should().Be(50m);
    }

    [Fact]
    public async Task CarrierBills_BelowAccrual_LeaveTheRestForTheNextBill()
    {
        using var db = await SeedAsync();
        var (vendorId, poId) = await AddPoWithFreightReceiptAsync(db, 50m);
        var service = ApService(db);
        var first = await AddFreightBillAsync(db, vendorId, 30m, poId, "BILL-1");
        await service.PostVendorBillApprovedAsync(first.Id, approvedByUserId: 7);
        var second = await AddFreightBillAsync(db, vendorId, 25m, poId, "BILL-2");
        await service.PostVendorBillApprovedAsync(second.Id, approvedByUserId: 7);

        var firstEntry = await BillEntryAsync(db, first.Id);
        Debit(firstEntry, FreightClearingId).Should().Be(30m);
        Debit(firstEntry, FreightInId).Should().Be(0m);

        var secondEntry = await BillEntryAsync(db, second.Id);
        Debit(secondEntry, FreightClearingId).Should().Be(20m);
        Debit(secondEntry, FreightInId).Should().Be(5m);
        (await service.OpenFreightAccrualAsync(BookId, poId, null, CancellationToken.None)).Should().Be(0m);
    }

    [Fact]
    public async Task PoClose_ReleasesTheUnbilledAccrualToFreightIn_Once()
    {
        using var db = await SeedAsync();
        var (vendorId, poId) = await AddPoWithFreightReceiptAsync(db, 50m);
        var service = ApService(db);
        var bill = await AddFreightBillAsync(db, vendorId, 30m, poId, "BILL-1");
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        await service.PostPurchaseOrderFreightCloseAsync(poId, new DateOnly(2026, 1, 25), 7);
        await service.PostPurchaseOrderFreightCloseAsync(poId, new DateOnly(2026, 1, 25), 7);

        var close = await db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == VendorBillApPostingService.PurchaseOrderFreightCloseSourceType);
        Debit(close, FreightClearingId).Should().Be(20m);
        Credit(close, FreightInId).Should().Be(20m);
        (await service.OpenFreightAccrualAsync(BookId, poId, null, CancellationToken.None)).Should().Be(0m);
    }

    [Fact]
    public async Task FreightWithoutPo_DebitsItsKey_FreightOutForDelivery()
    {
        using var db = await SeedAsync();
        var vendor = new Vendor { CompanyName = "Carrier Co", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var bill = await AddFreightBillAsync(db, vendor.Id, 80m, null, "BILL-9", key: "FREIGHT_OUT");

        await ApService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var entry = await BillEntryAsync(db, bill.Id);
        Debit(entry, FreightOutId).Should().Be(80m);
        Debit(entry, FreightClearingId).Should().Be(0m);
    }

    [Fact]
    public async Task VoidedFreightBill_NoLongerCountsAsCleared()
    {
        using var db = await SeedAsync();
        var (vendorId, poId) = await AddPoWithFreightReceiptAsync(db, 50m);
        var service = ApService(db);
        var bill = await AddFreightBillAsync(db, vendorId, 50m, poId, "BILL-1");
        await service.PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);
        (await service.OpenFreightAccrualAsync(BookId, poId, null, CancellationToken.None)).Should().Be(0m);

        bill.Status = VendorBillStatus.Void;
        await db.SaveChangesAsync();

        (await service.OpenFreightAccrualAsync(BookId, poId, null, CancellationToken.None)).Should().Be(50m);
    }

    [Fact]
    public async Task GrniReconciliation_ShowsTheOpenFreightAccrualByPo()
    {
        using var db = await SeedAsync();
        var (vendorId, poId) = await AddPoWithFreightReceiptAsync(db, 50m);
        var bill = await AddFreightBillAsync(db, vendorId, 30m, poId, "BILL-1");
        await ApService(db).PostVendorBillApprovedAsync(bill.Id, approvedByUserId: 7);

        var report = await new GrniReconciliationService(db, new SystemClock())
            .GetGrniReconciliationAsync(BookId, new DateOnly(2026, 1, 31));

        report.FreightClearingGlBalance.Should().Be(20m);
        report.FreightClearingOpen.Should().Be(20m);
        report.FreightClearingVariance.Should().Be(0m);
        var row = report.FreightClearing.Should().ContainSingle().Subject;
        row.PoNumber.Should().Be("PO-77");
        row.Accrued.Should().Be(50m);
        row.Billed.Should().Be(30m);
    }
}
