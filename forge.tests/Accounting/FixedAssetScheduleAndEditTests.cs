using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using AssetType = Forge.Core.Enums.AssetType;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Fixed-asset detail, edit and preview: the schedule lists posted months then projected months to the end of
/// life, an asset is editable only until its first depreciation posts, the preview of a month charges exactly
/// what the run then posts and posts nothing itself, and the link picker offers company-owned assets only.
/// </summary>
public class FixedAssetScheduleAndEditTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EquipId = 150;
    private const int AccumDepId = 151;
    private const int DepExpId = 600;
    private const int OtherExpId = 601;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static DepreciationService Service(AppDbContext db)
        => new(db, new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = 10, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = EquipId, BookId = BookId, AccountNumber = "15000", Name = "Equipment", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccumDepId, BookId = BookId, AccountNumber = "15900", Name = "Accumulated Depreciation", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = DepExpId, BookId = BookId, AccountNumber = "62000", Name = "Depreciation Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OtherExpId, BookId = BookId, AccountNumber = "62100", Name = "Tooling Depreciation", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateFixedAssetModel Model(
        decimal cost = 1200m, int life = 12, DepreciationMethod method = DepreciationMethod.StraightLine, string name = "CNC Mill") => new(
        BookId, name, "FA-001", cost, 0m, new DateOnly(2026, 1, 1), life, EquipId, AccumDepId, DepExpId, method);

    private static UpdateFixedAssetModel Edit(decimal cost, int life, int expenseAccountId = DepExpId) => new(
        "CNC Mill (rebuilt)", "FA-001A", cost, 100m, new DateOnly(2026, 2, 1), life, EquipId, AccumDepId, expenseAccountId);

    [Fact]
    public async Task Detail_of_a_new_asset_projects_every_month_of_its_life()
    {
        using var db = await SeedAsync();
        var asset = await Service(db).CreateAssetAsync(Model());

        var detail = await Service(db).GetAssetAsync(asset.Id);

        detail.IsEditable.Should().BeTrue();
        detail.DepreciationExpenseGlAccountId.Should().Be(DepExpId);
        detail.Schedule.Should().HaveCount(12).And.OnlyContain(r => !r.Posted && r.Amount == 100m);
        detail.Schedule[0].PeriodMonth.Should().Be(new DateOnly(2026, 1, 1));
        detail.Schedule[^1].AccumulatedDepreciation.Should().Be(1200m);
        detail.Schedule[^1].NetBookValue.Should().Be(0m);
    }

    [Fact]
    public async Task Detail_after_a_run_shows_the_posted_month_with_its_journal_then_the_rest_projected()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model());
        var run = await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        var detail = await svc.GetAssetAsync(asset.Id);

        detail.IsEditable.Should().BeFalse();
        detail.Schedule.Should().HaveCount(12);
        detail.Schedule[0].Posted.Should().BeTrue();
        detail.Schedule[0].JournalEntryId.Should().Be(run.JournalEntryIds!.Single());
        detail.Schedule.Skip(1).Should().OnlyContain(r => !r.Posted && r.JournalEntryId == null);
        detail.Schedule[1].PeriodMonth.Should().Be(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task Declining_balance_projection_matches_what_successive_runs_post()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model(cost: 1000m, life: 5, method: DepreciationMethod.DecliningBalance));
        var projected = (await svc.GetAssetAsync(asset.Id)).Schedule.Select(r => r.Amount).ToList();

        for (var month = 1; month <= 5; month++)
            await svc.RunDepreciationAsync(BookId, new DateOnly(2026, month, 1), 7);

        var posted = (await svc.GetAssetAsync(asset.Id)).Schedule.Where(r => r.Posted).Select(r => r.Amount).ToList();
        posted.Should().Equal(projected);
        posted.Sum().Should().Be(1000m);
    }

    [Fact]
    public async Task Update_before_any_depreciation_changes_the_asset()
    {
        using var db = await SeedAsync();
        var asset = await Service(db).CreateAssetAsync(Model());

        var updated = await Service(db).UpdateAssetAsync(asset.Id, Edit(cost: 2500m, life: 24, expenseAccountId: OtherExpId));

        updated.Asset.Name.Should().Be("CNC Mill (rebuilt)");
        updated.Asset.Cost.Should().Be(2500m);
        updated.Asset.SalvageValue.Should().Be(100m);
        updated.Asset.MonthlyDepreciation.Should().Be(100m);
        updated.DepreciationExpenseGlAccountId.Should().Be(OtherExpId);
        updated.Schedule.Should().HaveCount(24);
        (await db.FixedAssets.SingleAsync(a => a.Id == asset.Id)).InServiceDate.Should().Be(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task Update_after_depreciation_has_posted_is_refused()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model());
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        var act = () => svc.UpdateAssetAsync(asset.Id, Edit(cost: 5000m, life: 12));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can no longer be edited*");
        (await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).Cost.Should().Be(1200m);
    }

    [Fact]
    public async Task Update_applies_the_register_rules()
    {
        using var db = await SeedAsync();
        var asset = await Service(db).CreateAssetAsync(Model());

        var act = () => Service(db).UpdateAssetAsync(asset.Id, Edit(cost: 50m, life: 12));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Salvage value cannot exceed cost.");
    }

    [Fact]
    public async Task Update_of_an_unknown_asset_is_not_found()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).UpdateAssetAsync(999, Edit(cost: 1200m, life: 12));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Preview_charges_what_the_run_posts_and_posts_nothing()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        await svc.CreateAssetAsync(Model());
        await svc.CreateAssetAsync(Model(cost: 900m, life: 6, method: DepreciationMethod.DecliningBalance, name: "Press"));
        var month = new DateOnly(2026, 3, 15);

        var preview = await svc.PreviewDepreciationAsync(BookId, month);

        preview.PeriodMonth.Should().Be(new DateOnly(2026, 3, 1));
        preview.Lines.Select(l => l.Name).Should().Equal("CNC Mill", "Press");
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.DepreciationEntries.CountAsync()).Should().Be(0);

        var run = await svc.RunDepreciationAsync(BookId, month, 7);

        run.TotalAmount.Should().Be(preview.TotalAmount);
        run.AssetsDepreciated.Should().Be(preview.Lines.Count);
        run.JournalEntryIds.Should().HaveCount(2);
        var posted = await db.DepreciationEntries.Include(d => d.FixedAsset).ToListAsync();
        posted.Select(d => (d.FixedAsset.Name, d.Amount)).Should()
            .BeEquivalentTo(preview.Lines.Select(l => (l.Name, l.Amount)));
    }

    [Fact]
    public async Task Preview_skips_a_month_already_posted()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        await svc.CreateAssetAsync(Model());
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);

        var preview = await svc.PreviewDepreciationAsync(BookId, new DateOnly(2026, 1, 1));

        preview.Lines.Should().BeEmpty();
        preview.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Preview_for_an_unknown_book_is_not_found()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).PreviewDepreciationAsync(42, new DateOnly(2026, 1, 1));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Linkable_assets_are_company_owned_and_show_the_fixed_asset_already_booking_them()
    {
        using var db = await SeedAsync();
        var mold = new Asset { Name = "Mold A", AssetType = AssetType.Tooling, CurrentShotCount = 40 };
        var press = new Asset { Name = "Press 2", AssetType = AssetType.Machine, AcquisitionCost = 18000m };
        var customerMold = new Asset { Name = "Customer mold", AssetType = AssetType.Tooling, IsCustomerOwned = true };
        db.Assets.AddRange(mold, press, customerMold);
        await db.SaveChangesAsync();
        var linked = await Service(db).CreateAssetAsync(Model() with { LinkedAssetId = mold.Id });

        var options = await Service(db).ListLinkableAssetsAsync(BookId);

        options.Select(o => o.Name).Should().Equal("Mold A", "Press 2");
        options.Single(o => o.Id == mold.Id).LinkedFixedAssetId.Should().Be(linked.Id);
        options.Single(o => o.Id == press.Id).LinkedFixedAssetId.Should().BeNull();
        options.Single(o => o.Id == press.Id).AcquisitionCost.Should().Be(18000m);
    }

    [Fact]
    public async Task Disposal_preview_shows_the_catch_up_and_loss_the_disposal_then_posts()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model());
        await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        var date = new DateOnly(2026, 4, 15);
        var entriesBefore = await db.JournalEntries.IgnoreQueryFilters().CountAsync();

        var preview = await svc.PreviewDisposalAsync(asset.Id, date, 0m);

        preview.CatchUpDepreciation.Should().Be(200m);
        preview.NetBookValue.Should().Be(900m);
        preview.GainLoss.Should().Be(-900m);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(entriesBefore);

        var disposed = await svc.DisposeAssetAsync(
            asset.Id, new DisposeFixedAssetModel(date, 0m, null, OtherExpId), 7);

        disposed.CatchUpDepreciation.Should().Be(preview.CatchUpDepreciation);
        disposed.NetBookValue.Should().Be(preview.NetBookValue);
        disposed.GainLoss.Should().Be(preview.GainLoss);
    }

    [Fact]
    public async Task Disposal_preview_of_a_units_asset_counts_the_shots_not_yet_depreciated()
    {
        using var db = await SeedAsync();
        var mold = new Asset { Name = "Mold A", AssetType = AssetType.Tooling, CurrentShotCount = 250 };
        db.Assets.Add(mold);
        await db.SaveChangesAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model(method: DepreciationMethod.UnitsOfProduction) with
        {
            LinkedAssetId = mold.Id, UsefulLifeUnits = 1000m,
        });
        var date = new DateOnly(2026, 3, 10);

        var preview = await svc.PreviewDisposalAsync(asset.Id, date, 300m);

        preview.CatchUpDepreciation.Should().Be(300m);
        preview.NetBookValue.Should().Be(900m);
        preview.GainLoss.Should().Be(-600m);

        var disposed = await svc.DisposeAssetAsync(
            asset.Id, new DisposeFixedAssetModel(date, 0m, null, OtherExpId), 7);

        disposed.CatchUpDepreciation.Should().Be(preview.CatchUpDepreciation);
        disposed.NetBookValue.Should().Be(preview.NetBookValue);
    }

    [Fact]
    public async Task Disposal_preview_applies_the_disposal_rules()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var asset = await svc.CreateAssetAsync(Model());
        await svc.DisposeAssetAsync(asset.Id, new DisposeFixedAssetModel(new DateOnly(2026, 1, 20), 0m, null, OtherExpId), 7);

        var disposed = () => svc.PreviewDisposalAsync(asset.Id, new DateOnly(2026, 2, 1), 0m);
        var unknown = () => svc.PreviewDisposalAsync(999, new DateOnly(2026, 2, 1), 0m);

        await disposed.Should().ThrowAsync<InvalidOperationException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }
}
