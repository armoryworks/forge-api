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

using DepreciationMethod = Forge.Core.Enums.Accounting.DepreciationMethod;

namespace Forge.Tests.Accounting;

/// <summary>
/// FA-001 §10.3 — units-of-production depreciation by shot count for company-owned molds. Proves the
/// (Cost − Salvage) × units / UsefulLifeUnits charge incl. salvage, the LastDepreciatedUnits high-water
/// mark across runs, the remaining-book-value cap + FullyDepreciated flip, the zero-shots no-op, the
/// customer-owned link rejection, and the skip when the linked operational asset is soft-deleted.
/// </summary>
public class Phase4UnitsOfProductionDepreciationTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int EquipId = 150;
    private const int AccumDepId = 151;
    private const int DepExpId = 600;
    private const int MoldAssetId = 77;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static DepreciationService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<AppDbContext> SeedAsync(bool customerOwned = false, int shotCount = 0)
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = EquipId, BookId = BookId, AccountNumber = "15000", Name = "Tooling", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccumDepId, BookId = BookId, AccountNumber = "15900", Name = "Accumulated Depreciation", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = DepExpId, BookId = BookId, AccountNumber = "62000", Name = "Depreciation Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        // The operational mold whose shot counter drives units-of-production depreciation.
        db.Assets.Add(new Asset
        {
            Id = MoldAssetId, Name = "Mold M-100", AssetType = AssetType.Tooling,
            IsCustomerOwned = customerOwned, CavityCount = 4, ToolLifeExpectancy = 9000, CurrentShotCount = shotCount,
        });
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Cost 1,000 − salvage 100 over 9,000 expected shots.</summary>
    private static CreateFixedAssetModel UopModel(decimal cost = 1000m, decimal salvage = 100m, decimal lifeUnits = 9000m) => new(
        BookId, "Mold M-100", "FA-001", cost, salvage, new DateOnly(2026, 1, 1), 60,
        EquipId, AccumDepId, DepExpId,
        DepreciationMethod.UnitsOfProduction, lifeUnits, MoldAssetId);

    private static async Task BumpShotCountAsync(AppDbContext db, int shots)
    {
        var mold = await db.Assets.SingleAsync(a => a.Id == MoldAssetId);
        mold.CurrentShotCount = shots;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunDepreciation_UnitsOfProduction_ChargesCostLessSalvageByUnits()
    {
        using var db = await SeedAsync(shotCount: 1000);
        var svc = Service(db);
        await svc.CreateAssetAsync(UopModel()); // base 900 over 9,000 shots → 0.10/shot

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), postedByUserId: 7);

        result.AssetsDepreciated.Should().Be(1);
        result.TotalAmount.Should().Be(100m); // 900 × 1,000 / 9,000 — salvage excluded from the base
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "Depreciation");
        entry.Lines.Single(l => l.GlAccountId == DepExpId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == AccumDepId).Credit.Should().Be(100m);
    }

    [Fact]
    public async Task RunDepreciation_UnitsOfProduction_AdvancesHighWaterMarkAcrossRuns()
    {
        using var db = await SeedAsync(shotCount: 1000);
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(UopModel());

        var first = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        await BumpShotCountAsync(db, 2500);
        var second = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7);

        first.TotalAmount.Should().Be(100m); // 1,000 new shots
        second.TotalAmount.Should().Be(150m); // only the 1,500 shots above the high-water mark
        var asset = await db.FixedAssets.SingleAsync(a => a.Id == created.Id);
        asset.LastDepreciatedUnits.Should().Be(2500m);
    }

    [Fact]
    public async Task RunDepreciation_UnitsOfProduction_CapsAtBookValueAndFullyDepreciates()
    {
        using var db = await SeedAsync(shotCount: 12000); // beyond the 9,000-shot life
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(UopModel());

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        result.TotalAmount.Should().Be(900m); // capped at the depreciable base, not 900 × 12,000 / 9,000
        var asset = await db.FixedAssets.SingleAsync(a => a.Id == created.Id);
        asset.Status.Should().Be(FixedAssetStatus.FullyDepreciated);

        await BumpShotCountAsync(db, 15000);
        var after = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7);
        after.AssetsDepreciated.Should().Be(0); // nothing left to charge
    }

    [Fact]
    public async Task RunDepreciation_UnitsOfProduction_ZeroShots_NoEntryNoError()
    {
        using var db = await SeedAsync(shotCount: 0);
        var svc = Service(db);
        await svc.CreateAssetAsync(UopModel());

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        result.AssetsDepreciated.Should().Be(0);
        result.TotalAmount.Should().Be(0m);
        (await db.DepreciationEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsset_UnitsOfProduction_CustomerOwnedLink_IsRejected()
    {
        using var db = await SeedAsync(customerOwned: true);
        var act = () => Service(db).CreateAssetAsync(UopModel());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Customer-owned tooling cannot be capitalized*");
    }

    [Fact]
    public async Task CreateAsset_UnitsOfProduction_RequiresUsefulLifeUnitsAndLink()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        var noUnits = () => svc.CreateAssetAsync(UopModel() with { UsefulLifeUnits = 0m });
        await noUnits.Should().ThrowAsync<InvalidOperationException>().WithMessage("*useful life in units*");

        var noLink = () => svc.CreateAssetAsync(UopModel() with { LinkedAssetId = null });
        await noLink.Should().ThrowAsync<InvalidOperationException>().WithMessage("*linked operational asset*");
    }

    [Fact]
    public async Task RunDepreciation_UnitsOfProduction_DeletedLinkedAsset_IsSkipped()
    {
        using var db = await SeedAsync(shotCount: 1000);
        var svc = Service(db);
        await svc.CreateAssetAsync(UopModel());

        var mold = await db.Assets.SingleAsync(a => a.Id == MoldAssetId);
        mold.DeletedAt = DateTime.UtcNow; // soft delete — global filter hides it from the run
        await db.SaveChangesAsync();

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        result.AssetsDepreciated.Should().Be(0);
        (await db.DepreciationEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunDepreciation_MixedBook_StraightLineUnaffectedByUnitsOfProduction()
    {
        using var db = await SeedAsync(shotCount: 1000);
        var svc = Service(db);
        await svc.CreateAssetAsync(UopModel());
        await svc.CreateAssetAsync(new CreateFixedAssetModel(
            BookId, "CNC Mill", "FA-002", 1200m, 0m, new DateOnly(2026, 1, 1), 12,
            EquipId, AccumDepId, DepExpId)); // straight-line default → 100/mo

        var result = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        result.AssetsDepreciated.Should().Be(2);
        result.TotalAmount.Should().Be(200m); // 100 UoP + 100 SL
    }
}
