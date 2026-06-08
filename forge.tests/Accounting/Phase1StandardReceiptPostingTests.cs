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
/// Standard costing Phase 1 — PO receipt at STANDARD with material price variance (PPV). With a standard-cost
/// resolver wired, a stocked receipt capitalizes inventory at standard; the std−landed difference posts to
/// PURCHASE_PRICE_VARIANCE (favorable credit / unfavorable debit). The valuation store carries standard.
/// </summary>
public class Phase1StandardReceiptPostingTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int InvRawId = 130;
    private const int GrniId = 210;
    private const int FreightId = 220;
    private const int PpvId = 510;

    private static readonly DateOnly EntryDate = new(2026, 1, 15);

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

    private static ReceiptInventoryPostingService Service(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(true),
            valuation: new InventoryValuationService(db), standardCost: new StandardCostResolver(db));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FreightId, BookId = BookId, AccountNumber = "22000", Name = "Freight Clearing", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = PpvId, BookId = BookId, AccountNumber = "51000", Name = "Purchase Price Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "GRNI", GlAccountId = GrniId },
            new AccountDeterminationRule { BookId = BookId, Key = "FREIGHT_CLEARING", GlAccountId = FreightId },
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_PRICE_VARIANCE", GlAccountId = PpvId });
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Raw part with a standard unit cost; a PO line at the given PO price; a receiving record.</summary>
    private static async Task<int> AddReceiptAsync(AppDbContext db, decimal standardUnit, decimal poPrice, decimal qty, decimal? freight = null)
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy,
            ManualCostOverride = standardUnit,
        };
        db.Set<Part>().Add(part);
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = 1, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();

        var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = qty, UnitPrice = poPrice };
        db.Set<PurchaseOrderLine>().Add(line);
        await db.SaveChangesAsync();

        db.Set<ReceivingRecord>().Add(new ReceivingRecord { PurchaseOrderLineId = line.Id, QuantityReceived = qty, ReceiptNumber = "R-1", AllocatedFreight = freight });
        await db.SaveChangesAsync();
        return po.Id;
    }

    [Fact]
    public async Task Receipt_StandardAboveLanded_PostsFavorablePpv_AndCarriesStandard()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptAsync(db, standardUnit: 6m, poPrice: 5m, qty: 10m); // std 60, landed 50

        await Service(db).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(60m);   // at standard
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);    // at PO price
        entry.Lines.Single(l => l.GlAccountId == PpvId).Credit.Should().Be(10m);     // favorable (std > landed)
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));

        var store = await db.Set<InventoryValuation>().SingleAsync();
        store.TotalValue.Should().Be(60m, "the valuation store carries standard");
    }

    [Fact]
    public async Task Receipt_StandardBelowLanded_PostsUnfavorablePpv()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptAsync(db, standardUnit: 4m, poPrice: 5m, qty: 10m); // std 40, landed 50

        await Service(db).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(40m);
        entry.Lines.Single(l => l.GlAccountId == PpvId).Debit.Should().Be(10m); // unfavorable (landed > std)
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Receipt_WithFreight_PpvIsStandardMinusLanded()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptAsync(db, standardUnit: 6m, poPrice: 5m, qty: 10m, freight: 8m); // std 60, landed 58

        await Service(db).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(60m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(50m);
        entry.Lines.Single(l => l.GlAccountId == FreightId).Credit.Should().Be(8m);
        entry.Lines.Single(l => l.GlAccountId == PpvId).Credit.Should().Be(2m); // 60 − (50 + 8)
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Receipt_NoStandard_FallsBackToLanded_NoPpv()
    {
        using var db = await SeedAsync();
        var poId = await AddReceiptAsync(db, standardUnit: 0m, poPrice: 5m, qty: 10m); // no standard → landed 50

        await Service(db).PostReceiptAsync(poId, "R-1", EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(50m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == PpvId);
    }
}
