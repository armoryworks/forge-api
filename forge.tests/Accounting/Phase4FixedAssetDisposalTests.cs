using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using AssetType = Forge.Core.Enums.AssetType;
using DepreciationMethod = Forge.Core.Enums.Accounting.DepreciationMethod;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-4 — fixed-asset links, declining-balance depreciation and disposal. Proves the operational-asset link is
/// kept and returned for every method, double-declining charges on net book value and ends at salvage, and a
/// disposal catches up unposted months, writes cost and accumulated depreciation off, books the gain or loss and
/// stops further depreciation.
/// </summary>
public class Phase4FixedAssetDisposalTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int EquipId = 150;
    private const int AccumDepId = 151;
    private const int GainLossId = 910;
    private const int DepExpId = 600;
    private const int MachineAssetId = 77;
    private const int CustomerToolAssetId = 78;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static DepreciationService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<AppDbContext> SeedAsync(bool gainLossKey = true)
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
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = EquipId, BookId = BookId, AccountNumber = "15000", Name = "Equipment", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccumDepId, BookId = BookId, AccountNumber = "15900", Name = "Accumulated Depreciation", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = GainLossId, BookId = BookId, AccountNumber = "91000", Name = "Gain/Loss on Asset Disposal", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = DepExpId, BookId = BookId, AccountNumber = "62000", Name = "Depreciation Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        if (gainLossKey)
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = DepreciationService.KeyGainLossOnDisposal, GlAccountId = GainLossId });
        db.Assets.AddRange(
            new Asset { Id = MachineAssetId, Name = "5-axis mill", AssetType = AssetType.Machine },
            new Asset { Id = CustomerToolAssetId, Name = "Customer mold", AssetType = AssetType.Tooling, IsCustomerOwned = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateFixedAssetModel Model(
        decimal cost = 1200m,
        decimal salvage = 0m,
        int life = 12,
        DepreciationMethod method = DepreciationMethod.StraightLine,
        int? linkedAssetId = null,
        DateOnly? inService = null) => new(
        BookId, "CNC Mill", "FA-001", cost, salvage, inService ?? new DateOnly(2026, 1, 1), life,
        EquipId, AccumDepId, DepExpId, method, null, linkedAssetId);

    private static Task<JournalEntry> EntryAsync(AppDbContext db, string sourceType) =>
        db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync(e => e.SourceType == sourceType);

    [Fact]
    public async Task CreateAsset_StraightLine_KeepsAndReturnsLinkedAsset()
    {
        using var db = await SeedAsync();

        var created = await Service(db).CreateAssetAsync(Model(linkedAssetId: MachineAssetId));

        created.LinkedAssetId.Should().Be(MachineAssetId);
        created.Method.Should().Be(DepreciationMethod.StraightLine);
        (await db.FixedAssets.SingleAsync(a => a.Id == created.Id)).LinkedAssetId.Should().Be(MachineAssetId);
        (await Service(db).ListAssetsAsync(BookId)).Single().LinkedAssetId.Should().Be(MachineAssetId);
    }

    [Fact]
    public async Task CreateAsset_StraightLine_LinkedCustomerOwned_IsRejected()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).CreateAssetAsync(Model(linkedAssetId: CustomerToolAssetId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Customer-owned*");
    }

    [Fact]
    public async Task CreateAsset_StraightLine_UnknownLinkedAsset_Throws()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).CreateAssetAsync(Model(linkedAssetId: 9999));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DecliningBalance_FirstTwoMonths_ChargeTwiceTheRateOnBookValue()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 240_000m, salvage: 24_000m, life: 120, method: DepreciationMethod.DecliningBalance));

        created.MonthlyDepreciation.Should().Be(4_000m);
        var first = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        var second = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7);

        first.TotalAmount.Should().Be(4_000m);
        second.TotalAmount.Should().Be(3_933.33m);
        (await svc.ListAssetsAsync(BookId)).Single().MonthlyDepreciation.Should().Be(3_867.78m);
    }

    [Fact]
    public async Task DecliningBalance_SwitchesToStraightLine_AndEndsAtSalvageOnTheLastMonth()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 1_200m, salvage: 0m, life: 12, method: DepreciationMethod.DecliningBalance));

        var charges = new List<decimal>();
        for (var m = 1; m <= 12; m++)
            charges.Add((await svc.RunDepreciationAsync(BookId, new DateOnly(2026, m, 1), 7)).TotalAmount);

        charges[0].Should().Be(200m);
        charges.Should().OnlyContain(c => c > 0m);
        charges.Skip(6).Should().OnlyContain(c => Math.Abs(c - charges[6]) <= 0.01m);
        charges.Sum().Should().Be(1_200m);
        var asset = (await svc.ListAssetsAsync(BookId)).Single(a => a.Id == created.Id);
        asset.NetBookValue.Should().Be(0m);
        asset.Status.Should().Be(FixedAssetStatus.FullyDepreciated);
    }

    [Fact]
    public async Task DecliningBalance_NeverDropsBelowSalvage()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 1_200m, salvage: 200m, life: 12, method: DepreciationMethod.DecliningBalance));

        for (var m = 1; m <= 12; m++)
            await svc.RunDepreciationAsync(BookId, new DateOnly(2026, m, 1), 7);

        var asset = (await svc.ListAssetsAsync(BookId)).Single(a => a.Id == created.Id);
        asset.AccumulatedDepreciation.Should().Be(1_000m);
        asset.NetBookValue.Should().Be(200m);
        asset.Status.Should().Be(FixedAssetStatus.FullyDepreciated);
    }

    [Fact]
    public async Task Dispose_AtLoss_CatchesUpMissedMonths_AndPostsCostOutAccumulatedInAndLoss()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 1_200m, life: 12, linkedAssetId: MachineAssetId));
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7);

        var result = await svc.DisposeAssetAsync(created.Id,
            new DisposeFixedAssetModel(new DateOnly(2026, 5, 15), 500m, CashId), 7);

        result.CatchUpDepreciation.Should().Be(200m);
        result.NetBookValue.Should().Be(800m);
        result.GainLoss.Should().Be(-300m);
        result.Asset.Status.Should().Be(FixedAssetStatus.Disposed);

        var disposal = await EntryAsync(db, DepreciationService.DisposalSourceType);
        disposal.Id.Should().Be(result.JournalEntryId);
        disposal.SourceId.Should().Be(created.Id);
        disposal.Lines.Single(l => l.GlAccountId == AccumDepId).Debit.Should().Be(400m);
        disposal.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(500m);
        disposal.Lines.Single(l => l.GlAccountId == EquipId).Credit.Should().Be(1_200m);
        disposal.Lines.Single(l => l.GlAccountId == GainLossId).Debit.Should().Be(300m);
        disposal.Lines.Sum(l => l.Debit).Should().Be(disposal.Lines.Sum(l => l.Credit));

        (await db.ActivityLogs.CountAsync(a => a.Action == "disposed" && a.EntityType == "Asset" && a.EntityId == MachineAssetId)).Should().Be(1);
    }

    [Fact]
    public async Task Dispose_AtGain_CreditsGain()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 1_200m, life: 12));
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        var result = await svc.DisposeAssetAsync(created.Id,
            new DisposeFixedAssetModel(new DateOnly(2026, 2, 10), 1_150m, CashId), 7);

        result.CatchUpDepreciation.Should().Be(0m);
        result.GainLoss.Should().Be(50m);
        (await EntryAsync(db, DepreciationService.DisposalSourceType)).Lines
            .Single(l => l.GlAccountId == GainLossId).Credit.Should().Be(50m);
    }

    [Fact]
    public async Task Dispose_FullyDepreciated_NoProceeds_HasNoGainLossLine()
    {
        using var db = await SeedAsync(gainLossKey: false);
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 100m, life: 2));
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 2, 1), 7);

        var result = await svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 3, 1)), 7);

        result.GainLoss.Should().Be(0m);
        var lines = (await EntryAsync(db, DepreciationService.DisposalSourceType)).Lines;
        lines.Should().HaveCount(2);
        lines.Should().NotContain(l => l.GlAccountId == GainLossId);
    }

    [Fact]
    public async Task Dispose_Twice_IsRejected()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());
        await svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 1, 20)), 7);

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 1, 21)), 7);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been disposed*");
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.SourceType == DepreciationService.DisposalSourceType)).Should().Be(1);
    }

    [Fact]
    public async Task Dispose_ThenRunDepreciation_PostsNothingForThatAsset()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());
        await svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 3, 5)), 7);

        var run = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 4, 1), 7);

        run.AssetsDepreciated.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_BeforeInServiceDate_IsRejected()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(inService: new DateOnly(2026, 3, 1)));

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 2, 1)), 7);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Dispose_WithProceeds_RequiresProceedsAccount()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 2, 1), 100m), 7);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*proceeds*");
    }

    [Fact]
    public async Task Dispose_ProceedsToExpenseAccount_IsRejected()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 2, 1), 100m, DepExpId), 7);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Dispose_WithLossAndNoGainLossAccount_IsRejected()
    {
        using var db = await SeedAsync(gainLossKey: false);
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 2, 1)), 7);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*gain or loss*");
    }

    [Fact]
    public async Task Dispose_BeforeAPostedMonth_IsRejected()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model());
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 4, 1), 7);

        var act = () => svc.DisposeAssetAsync(created.Id, new DisposeFixedAssetModel(new DateOnly(2026, 3, 10)), 7);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*2026-04*");
    }

    [Fact]
    public async Task Dispose_DecliningBalance_CatchUpFollowsTheSchedule()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAssetAsync(Model(cost: 240_000m, salvage: 24_000m, life: 120, method: DepreciationMethod.DecliningBalance));

        var result = await svc.DisposeAssetAsync(created.Id,
            new DisposeFixedAssetModel(new DateOnly(2026, 3, 3), 230_000m, CashId), 7);

        result.CatchUpDepreciation.Should().Be(7_933.33m);
        result.NetBookValue.Should().Be(232_066.67m);
        result.GainLoss.Should().Be(-2_066.67m);
    }
}
