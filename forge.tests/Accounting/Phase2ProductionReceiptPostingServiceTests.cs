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
/// Phase-2 STAGE E — production receipt (job-complete→FG). Proves: DARK by default; a received run posts
/// Dr INVENTORY_FG / Cr INVENTORY_WIP at standard cost and feeds the FG valuation store; no standard cost or
/// a subassembly (same-account) output skips the GL; non-stocked no-op; idempotent.
/// </summary>
public class Phase2ProductionReceiptPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int InvRawId = 130;
    private const int InvWipId = 131;
    private const int InvFgId = 132;

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

    private static ProductionReceiptPostingService Service(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), valuation: new InventoryValuationService(db));

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
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvWipId, BookId = BookId, AccountNumber = "13200", Name = "Inventory — WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — FG", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = InvWipId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InvFgId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Adds a Part of the given class and a received ProductionRun; returns the run id.</summary>
    private static async Task<int> AddReceivedRunAsync(
        AppDbContext db, InventoryClass cls, decimal? standardCost, int receivedQty)
    {
        var part = new Part
        {
            PartNumber = $"P-{cls}-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = cls, ProcurementSource = ProcurementSource.Make,
            ManualCostOverride = standardCost,
        };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();

        var run = new ProductionRun
        {
            JobId = 555, PartId = part.Id, RunNumber = $"RUN-{Guid.NewGuid():N}",
            TargetQuantity = receivedQty, CompletedQuantity = receivedQty, ReceivedQuantity = receivedQty,
            ReceivedToStockAt = DateTimeOffset.UtcNow, Status = ProductionRunStatus.Completed,
        };
        db.Set<ProductionRun>().Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task Receipt_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var runId = await AddReceivedRunAsync(db, InventoryClass.FinishedGood, standardCost: 12m, receivedQty: 5);

        await Service(db, fullGlOn: false).PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_FinishedGood_PostsFgFromStandardCost_AndFeedsStore()
    {
        using var db = await SeedAsync();
        var runId = await AddReceivedRunAsync(db, InventoryClass.FinishedGood, standardCost: 12m, receivedQty: 5);
        var run = await db.Set<ProductionRun>().FindAsync(runId);

        await Service(db, fullGlOn: true).PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.Inventory);
        entry.SourceType.Should().Be("ProductionRun");
        entry.Lines.Single(l => l.GlAccountId == InvFgId).Debit.Should().Be(60m);   // 12 × 5
        entry.Lines.Single(l => l.GlAccountId == InvWipId).Credit.Should().Be(60m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));

        var store = await db.Set<InventoryValuation>().SingleAsync(v => v.PartId == run!.PartId);
        store.OnHandQuantity.Should().Be(5m);
        store.TotalValue.Should().Be(60m);
        store.AverageUnitCost.Should().Be(12m);
    }

    [Fact]
    public async Task Receipt_NoStandardCost_SkipsGl()
    {
        using var db = await SeedAsync();
        var runId = await AddReceivedRunAsync(db, InventoryClass.FinishedGood, standardCost: null, receivedQty: 5);

        await Service(db, fullGlOn: true).PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.Set<InventoryValuation>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_Subassembly_SkipsGl_SameAccountWash()
    {
        using var db = await SeedAsync();
        // A subassembly debits INVENTORY_WIP — the same account it credits — so the GL is skipped.
        var runId = await AddReceivedRunAsync(db, InventoryClass.Subassembly, standardCost: 9m, receivedQty: 3);

        await Service(db, fullGlOn: true).PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_CalledTwice_IsIdempotent_StoreFedOnce()
    {
        using var db = await SeedAsync();
        var runId = await AddReceivedRunAsync(db, InventoryClass.FinishedGood, standardCost: 12m, receivedQty: 5);
        var run = await db.Set<ProductionRun>().FindAsync(runId);
        var service = Service(db, fullGlOn: true);

        await service.PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);
        await service.PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var store = await db.Set<InventoryValuation>().SingleAsync(v => v.PartId == run!.PartId);
        store.OnHandQuantity.Should().Be(5m, "the store is fed exactly once despite the re-post");
    }

    [Fact]
    public async Task Receipt_NonStockedPart_IsNoOp()
    {
        using var db = await SeedAsync();
        var runId = await AddReceivedRunAsync(db, InventoryClass.Consumable, standardCost: 4m, receivedQty: 5);

        await Service(db, fullGlOn: true).PostProductionReceiptAsync(runId, EntryDate, receivedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }
}
