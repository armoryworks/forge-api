using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-4 — fixed-asset straight-line depreciation. Proves the monthly amount, the Dr expense / Cr
/// accumulated posting, idempotency per asset-month, and the final-month remainder + FullyDepreciated flip.
/// </summary>
public class Phase4DepreciationServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int EquipId = 150;
    private const int AccumDepId = 151;
    private const int DepExpId = 600;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static DepreciationService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        // A full year of monthly periods so depreciation can post across months.
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = EquipId, BookId = BookId, AccountNumber = "15000", Name = "Equipment", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccumDepId, BookId = BookId, AccountNumber = "15900", Name = "Accumulated Depreciation", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = DepExpId, BookId = BookId, AccountNumber = "62000", Name = "Depreciation Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateFixedAssetModel AssetModel(decimal cost = 1200m, decimal salvage = 0m, int life = 12) => new(
        BookId, "CNC Mill", "FA-001", cost, salvage, new DateOnly(2026, 1, 1), life,
        EquipId, AccumDepId, DepExpId);

    [Fact]
    public async Task CreateAsset_ComputesMonthlyStraightLine()
    {
        using var db = await SeedAsync();
        var asset = await Service(db).CreateAssetAsync(AssetModel(cost: 1200m, salvage: 0m, life: 12));
        asset.MonthlyDepreciation.Should().Be(100m);
        asset.NetBookValue.Should().Be(1200m);
    }

    [Fact]
    public async Task RunDepreciation_PostsExpenseAndAccumulated()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(AssetModel());

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 3, 1), postedByUserId: 7);

        result.AssetsDepreciated.Should().Be(1);
        result.TotalAmount.Should().Be(100m);
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "Depreciation");
        entry.Lines.Single(l => l.GlAccountId == DepExpId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == AccumDepId).Credit.Should().Be(100m);
    }

    [Fact]
    public async Task RunDepreciation_SameMonth_IsIdempotent()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        await svc.CreateAssetAsync(AssetModel());

        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 3, 1), 7);
        var second = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 3, 1), 7);

        second.AssetsDepreciated.Should().Be(0); // already posted
        (await db.DepreciationEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunDepreciation_FinalMonth_RemainderThenFullyDepreciated()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(AssetModel(cost: 100m, salvage: 0m, life: 2)); // 50/mo

        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7); // 50
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7); // 50 → fully depreciated
        var third = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 3, 1), 7); // nothing left

        third.AssetsDepreciated.Should().Be(0);
        var list = await svc.ListAssetsAsync(BookId);
        var a = list.Single(x => x.Id == asset.Id);
        a.AccumulatedDepreciation.Should().Be(100m);
        a.NetBookValue.Should().Be(0m);
        a.Status.Should().Be(FixedAssetStatus.FullyDepreciated);
    }
}
