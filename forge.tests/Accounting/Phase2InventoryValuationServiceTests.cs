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
/// Phase-2 STAGE E — perpetual inventory valuation store. Proves the weighted-average math (receipt feed +
/// issue relief) and that a receipt posted through the GL feeds the store so it ties to the GL inventory
/// control balance.
/// </summary>
public class Phase2InventoryValuationServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;
    private const int InvRawId = 130;
    private const int GrniId = 210;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool on) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = on }, DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static InventoryValuationService Valuation(AppDbContext db) => new(db);

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<int> AddPartAsync(AppDbContext db)
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    [Fact]
    public async Task ApplyReceipt_SetsRunningAverage()
    {
        using var db = await SeedAsync();
        var partId = await AddPartAsync(db);
        var svc = Valuation(db);

        await svc.ApplyReceiptAsync(BookId, partId, quantity: 10m, totalCost: 50m);

        var row = await db.InventoryValuations.SingleAsync(v => v.PartId == partId);
        row.OnHandQuantity.Should().Be(10m);
        row.TotalValue.Should().Be(50m);
        row.AverageUnitCost.Should().Be(5m);
    }

    [Fact]
    public async Task ApplyReceipt_Twice_WeightedAverages()
    {
        using var db = await SeedAsync();
        var partId = await AddPartAsync(db);
        var svc = Valuation(db);

        await svc.ApplyReceiptAsync(BookId, partId, 10m, 50m); // avg 5
        await svc.ApplyReceiptAsync(BookId, partId, 10m, 70m); // +700/... → 120/20 = 6

        var row = await db.InventoryValuations.SingleAsync(v => v.PartId == partId);
        row.OnHandQuantity.Should().Be(20m);
        row.TotalValue.Should().Be(120m);
        row.AverageUnitCost.Should().Be(6m);
    }

    [Fact]
    public async Task ApplyIssue_RelievesAtAverage()
    {
        using var db = await SeedAsync();
        var partId = await AddPartAsync(db);
        var svc = Valuation(db);
        await svc.ApplyReceiptAsync(BookId, partId, 10m, 50m); // avg 5

        var relieved = await svc.ApplyIssueAsync(BookId, partId, 4m);

        relieved.Should().Be(20m); // 4 × 5
        var row = await db.InventoryValuations.SingleAsync(v => v.PartId == partId);
        row.OnHandQuantity.Should().Be(6m);
        row.TotalValue.Should().Be(30m);
    }

    [Fact]
    public async Task Receipt_FeedsStore_AndReconcilesToGl()
    {
        using var db = await SeedAsync();
        // Accounting scaffolding for the GL receipt post.
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId });
        await db.SaveChangesAsync();

        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Description = "x", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.Set<Part>().Add(part);
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = 1, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        var poLine = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 10m, UnitPrice = 5m };
        db.Set<PurchaseOrderLine>().Add(poLine);
        await db.SaveChangesAsync();
        db.Set<ReceivingRecord>().Add(new ReceivingRecord { PurchaseOrderLineId = poLine.Id, QuantityReceived = 10m, ReceiptNumber = "R-1" });
        await db.SaveChangesAsync();

        var engine = new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());
        var valuation = Valuation(db);
        var receiptService = new ReceiptInventoryPostingService(db, engine, new FakeCapabilities(on: true), null, valuation);

        await receiptService.PostReceiptAsync(po.Id, "R-1", new DateOnly(2026, 1, 15), receivedByUserId: 7);

        // Store fed at landed cost (10 × 5 = 50), and it ties to the GL inventory-control balance.
        var row = await db.InventoryValuations.SingleAsync(v => v.PartId == part.Id);
        row.OnHandQuantity.Should().Be(10m);
        row.TotalValue.Should().Be(50m);

        var recon = await valuation.ReconcileAsync(BookId);
        recon.StoreValue.Should().Be(50m);
        recon.GlInventoryBalance.Should().Be(50m);
        recon.IsReconciled.Should().BeTrue();
    }
}
