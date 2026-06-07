using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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
/// Phase-2 STAGE D.3 — GRNI reconciliation + aging. Proves on the InMemory provider that the service ties
/// the GL GRNI balance (receipt accruals Cr − bill clears Dr) to the operational received-but-not-billed
/// position (Σ open-qty × PO price), ages the open accrual by receipt date, flags the variance when a
/// receipt wasn't accrued, and sweeps line-level uncovered receipts.
/// </summary>
public class Phase2GrniReconciliationServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int GrniId = 210;
    private const int OffsetId = 201;

    private static readonly DateOnly AsOf = new(2026, 3, 1);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static GrniReconciliationService Service(AppDbContext db)
        => new(db, new SystemClock());

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OffsetId, BookId = BookId, AccountNumber = "13100", Name = "Inventory (offset)", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId },
            new AccountDeterminationRule { BookId = BookId, Key = "OFFSET", GlAccountId = OffsetId });

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

    /// <summary>Adds a PO + one line (received/billed at unitPrice) + a receiving record dated receivedOn.</summary>
    private static async Task<(int poId, int poLineId)> AddPoLineWithReceiptAsync(
        AppDbContext db, int vendorId, decimal unitPrice, decimal received, decimal billed,
        DateOnly receivedOn, string? receiptNumber = "R-1")
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Part>().Add(part);
        var po = new PurchaseOrder { PONumber = $"PO-{Guid.NewGuid():N}"[..10], VendorId = vendorId, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();

        var poLine = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id,
            OrderedQuantity = received, ReceivedQuantity = received, BilledQuantity = billed, UnitPrice = unitPrice,
        };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();

        if (received > 0m)
        {
            db.Set<ReceivingRecord>().Add(new ReceivingRecord
            {
                PurchaseOrderLineId = poLine.Id, QuantityReceived = received, ReceiptNumber = receiptNumber,
                CreatedAt = new DateTimeOffset(receivedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }
        return (po.Id, poLine.Id);
    }

    private static async Task PostGrniReceiptAsync(AppDbContext db, int poId, string receiptNumber, decimal amount, DateOnly date)
        => await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = date, Source = JournalSource.Inventory, SourceType = "Receipt", SourceId = poId,
            CurrencyId = UsdId, Memo = "receipt accrual",
            IdempotencyKey = $"{JournalSource.Inventory}:Receipt:{poId}:{receiptNumber}:RECEIPT",
            Lines =
            [
                new PostingLine { AccountKey = "OFFSET", Debit = amount, Description = "inventory" },
                new PostingLine { AccountKey = "GRNI", Credit = amount, Description = "grni" },
            ],
        }, 7);

    private static async Task PostGrniClearAsync(AppDbContext db, int billId, decimal amount, DateOnly date)
        => await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = date, Source = JournalSource.AP, SourceType = "VendorBill", SourceId = billId,
            CurrencyId = UsdId, Memo = "bill clear",
            IdempotencyKey = $"{JournalSource.AP}:VendorBill:{billId}:BILL",
            Lines =
            [
                new PostingLine { AccountKey = "GRNI", Debit = amount, Description = "grni clear" },
                new PostingLine { AccountKey = "OFFSET", Credit = amount, Description = "ap" },
            ],
        }, 7);

    [Fact]
    public async Task FullyAccruedUnbilled_IsReconciled()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var (poId, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 10m, billed: 0m, receivedOn: AsOf);
        await PostGrniReceiptAsync(db, poId, "R-1", amount: 50m, date: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.GlBalance.Should().Be(50m);
        r.OperationalOpen.Should().Be(50m);
        r.Variance.Should().Be(0m);
        r.IsReconciled.Should().BeTrue();
        r.PurchaseOrders.Should().ContainSingle().Which.OpenAmount.Should().Be(50m);
        r.UncoveredReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task PartiallyBilled_NetsOpenGrni()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var (poId, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 10m, billed: 4m, receivedOn: AsOf);
        await PostGrniReceiptAsync(db, poId, "R-1", amount: 50m, date: AsOf); // accrue all received
        await PostGrniClearAsync(db, billId: 900, amount: 20m, date: AsOf);   // clear 4 × 5

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.GlBalance.Should().Be(30m);        // 50 − 20
        r.OperationalOpen.Should().Be(30m);  // (10 − 4) × 5
        r.IsReconciled.Should().BeTrue();
        r.PurchaseOrders.Should().ContainSingle().Which.OpenAmount.Should().Be(30m);
    }

    [Fact]
    public async Task ReceiptNotAccrued_ShowsVarianceAndUncovered()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // Received + a receiving record exist, but NO GRNI accrual was posted (e.g. received while FULLGL off).
        await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 10m, billed: 0m, receivedOn: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.GlBalance.Should().Be(0m);
        r.OperationalOpen.Should().Be(50m);
        r.Variance.Should().Be(-50m);
        r.IsReconciled.Should().BeFalse();
        r.UncoveredReceipts.Should().ContainSingle().Which.Reason.Should().Be("NO_ACCRUAL_POSTED");
    }

    [Fact]
    public async Task NullReceiptNumber_IsUncovered()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 4m, billed: 0m, receivedOn: AsOf, receiptNumber: null);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.UncoveredReceipts.Should().ContainSingle().Which.Reason.Should().Be("NO_RECEIPT_NUMBER");
    }

    [Fact]
    public async Task FullyBilledLine_NotOpen_AndReconciledAtZero()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        var (poId, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 10m, billed: 10m, receivedOn: AsOf);
        await PostGrniReceiptAsync(db, poId, "R-1", amount: 50m, date: AsOf);
        await PostGrniClearAsync(db, billId: 901, amount: 50m, date: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.GlBalance.Should().Be(0m);
        r.OperationalOpen.Should().Be(0m);
        r.IsReconciled.Should().BeTrue();
        r.PurchaseOrders.Should().BeEmpty();
        r.UncoveredReceipts.Should().BeEmpty(); // accrual + clear both posted
    }

    [Fact]
    public async Task SubCentResidue_IsReconciledWithinTolerance()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // Operational open = 3 × 1.111 = 3.333; GL accrued at a currency-rounded 3.33 → residue 0.003 ≤ 0.01.
        var (poId, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 1.111m, received: 3m, billed: 0m, receivedOn: AsOf);
        await PostGrniReceiptAsync(db, poId, "R-1", amount: 3.33m, date: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.OperationalOpen.Should().Be(3.333m);
        r.GlBalance.Should().Be(3.33m);
        r.Variance.Should().Be(-0.003m);
        r.IsReconciled.Should().BeTrue(); // within the 0.01 book tolerance
    }

    [Fact]
    public async Task Aging_BucketsOpenAmountByReceiptDate()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // Line A received today (0-30), line B received 40 days before AsOf (31-60); both unbilled.
        var (poA, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 2m, billed: 0m, receivedOn: AsOf);
        var (poB, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 4m, billed: 0m, receivedOn: AsOf.AddDays(-40));
        await PostGrniReceiptAsync(db, poA, "R-1", amount: 10m, date: AsOf);
        await PostGrniReceiptAsync(db, poB, "R-1", amount: 20m, date: AsOf.AddDays(-40));

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.OperationalOpen.Should().Be(30m);
        r.IsReconciled.Should().BeTrue();
        BucketAmount(r.TotalsByBucket, "0-30").Should().Be(10m);
        BucketAmount(r.TotalsByBucket, "31-60").Should().Be(20m);
    }

    [Fact]
    public async Task Aging_ReceiptInPriorYear_BucketsByActualDayCount()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // Receiving record dated 2025-12-15; AsOf 2026-03-01 → 76 days across the year boundary → 61-90.
        // (DateOnly.DayNumber is days-since-0001, NOT day-of-year, so cross-year subtraction is correct.)
        var (poId, _) = await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 5m, received: 4m, billed: 0m, receivedOn: new DateOnly(2025, 12, 15));
        await PostGrniReceiptAsync(db, poId, "R-1", amount: 20m, date: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.OperationalOpen.Should().Be(20m);
        BucketAmount(r.TotalsByBucket, "61-90").Should().Be(20m);
        BucketAmount(r.TotalsByBucket, "0-30").Should().Be(0m);
    }

    [Fact]
    public async Task Aging_MultipleReceipts_AgesWholeOpenAtEarliestDate()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // One PO line, two receipts (3 units 40 days ago, 2 units today), all unbilled. The whole open amount
        // ages at the earliest receipt date (documented conservative simplification) → all 50 in 31-60.
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.Set<Part>().Add(part);
        var po = new PurchaseOrder { PONumber = "PO-MR", VendorId = vendorId, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var poLine = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 5m, ReceivedQuantity = 5m, BilledQuantity = 0m, UnitPrice = 10m };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();
        db.Set<ReceivingRecord>().AddRange(
            new ReceivingRecord { PurchaseOrderLineId = poLine.Id, QuantityReceived = 3m, ReceiptNumber = "R-1", CreatedAt = new DateTimeOffset(AsOf.AddDays(-40).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) },
            new ReceivingRecord { PurchaseOrderLineId = poLine.Id, QuantityReceived = 2m, ReceiptNumber = "R-2", CreatedAt = new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) });
        await db.SaveChangesAsync();
        await PostGrniReceiptAsync(db, po.Id, "R-1", amount: 30m, date: AsOf.AddDays(-40));
        await PostGrniReceiptAsync(db, po.Id, "R-2", amount: 20m, date: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.OperationalOpen.Should().Be(50m);
        r.IsReconciled.Should().BeTrue();
        BucketAmount(r.TotalsByBucket, "31-60").Should().Be(50m);
        BucketAmount(r.TotalsByBucket, "0-30").Should().Be(0m);
    }

    [Fact]
    public async Task ZeroPricedOpenLine_ContributesNoGrniAndIsNotFlaggedUncovered()
    {
        using var db = await SeedAsync();
        var vendorId = await AddVendorAsync(db);
        // Received 5 @ price 0 → no GRNI to accrue. It must not appear in open GRNI, nor as an uncovered receipt.
        await AddPoLineWithReceiptAsync(db, vendorId, unitPrice: 0m, received: 5m, billed: 0m, receivedOn: AsOf);

        var r = await Service(db).GetGrniReconciliationAsync(BookId, AsOf);

        r.OperationalOpen.Should().Be(0m);
        r.GlBalance.Should().Be(0m);
        r.IsReconciled.Should().BeTrue();
        r.PurchaseOrders.Should().BeEmpty();
        r.UncoveredReceipts.Should().BeEmpty();
    }

    private static decimal BucketAmount(IReadOnlyList<GrniAgingBucket> buckets, string label)
        => buckets.Single(b => b.Label == label).Amount;
}
