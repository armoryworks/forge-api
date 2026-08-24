using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Costing.Tier3;
using Forge.Core.Entities.Accounting;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

/// <summary>
/// The prepackaged costing setup: a few answers populate the overhead pools with
/// annual budgets over direct labor hours, and (FULLGL on) mirror them into GL
/// expense accounts + budget lines. Proves the burden math, the FULLGL-off skip
/// note, idempotent re-apply on the GL side, and zero-amount pool skipping.
/// </summary>
public class CostingQuickStartTests
{
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ApplyCostingQuickStartRequestModel Answers(decimal equipment = 24_000m) => new()
    {
        FiscalYear = 2026,
        DirectHeadcount = 5m,
        AverageHourlyWage = 25m,
        PayrollTaxPercent = 8m,
        BenefitsMonthlyPerEmployee = 500m,
        UtilitiesMonthly = 2_000m,
        FacilitiesMonthly = 6_000m,
        EquipmentAnnual = equipment,
    };

    /// <summary>Seeds the reusable substrate so the handler exercises the reuse path
    /// (mediator is only consulted for the budget upserts, which the mock captures).</summary>
    private static async Task<(AppDbContext Db, int PeriodId)> SeedCostingAsync(bool withBook)
    {
        var db = TestDbContextFactory.Create();
        var center = new CostingCostCenter { Code = "PLANT", Name = "Plant", Type = CostCenterType.Production };
        db.CostingCostCenters.Add(center);
        var period = new CostingPeriod
        {
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        };
        db.CostingPeriods.Add(period);
        await db.SaveChangesAsync();

        foreach (var code in new[] { "UTIL", "FAC", "BURDEN", "EQUIP" })
        {
            db.OverheadCostPools.Add(new OverheadCostPool
            {
                CostingCostCenterId = center.Id, Code = code, Name = code,
                Behavior = OverheadBehavior.Fixed, Driver = OverheadDriver.LaborHour,
            });
        }
        if (withBook)
        {
            db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
            db.Set<Book>().Add(new Book
            {
                Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1,
                ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            });
        }
        await db.SaveChangesAsync();
        return (db, period.Id);
    }

    private static Mock<IMediator> CapturingMediator(List<UpsertOverheadPoolBudgetCommand> captured)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<UpsertOverheadPoolBudgetCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<OverheadPoolBudgetResponseModel>, CancellationToken>((cmd, _) =>
                captured.Add((UpsertOverheadPoolBudgetCommand)cmd))
            .ReturnsAsync((OverheadPoolBudgetResponseModel)null!);
        return mediator;
    }

    [Fact]
    public async Task QuickStart_PopulatesPoolBudgets_AndGlBudgets()
    {
        var (db, _) = await SeedCostingAsync(withBook: true);
        var captured = new List<UpsertOverheadPoolBudgetCommand>();
        var handler = new ApplyCostingQuickStartHandler(db, CapturingMediator(captured).Object, new FakeCapabilities(true));

        var result = await handler.Handle(new ApplyCostingQuickStartCommand(Answers()), CancellationToken.None);

        // 5 heads × 2080 = 10,400 hours; wages 260,000.
        result.AnnualDirectLaborHours.Should().Be(10_400m);
        captured.Should().HaveCount(4);
        var byAmount = captured.Select(c => c.BudgetAmount).ToList();
        byAmount.Should().Contain(24_000m);   // utilities 2000 × 12
        byAmount.Should().Contain(72_000m);   // facilities 6000 × 12
        byAmount.Should().Contain(50_800m);   // burden: 260,000 × 8% + 500 × 12 × 5
        captured.Should().OnlyContain(c => c.BudgetDriverQty == 10_400m);

        // GL half: category accounts created + full-year budget lines mirrored.
        result.GlBudgetsCreated.Should().BeTrue();
        var accounts = await db.GlAccounts.Where(a => a.AccountNumber.StartsWith("60")).ToListAsync();
        accounts.Select(a => a.AccountNumber).Should().Contain(["60100", "60200", "60300", "60500"]);
        var budgets = await db.AcctBudgets.ToListAsync();
        budgets.Should().HaveCount(4);
        budgets.Should().OnlyContain(b => b.FiscalYear == 2026 && b.PeriodMonth == null);
        budgets.Sum(b => b.Amount).Should().Be(result.TotalAnnualOverhead);

        // Total 24k + 72k + 50.8k + 24k = 170,800 → 16.42/hr.
        result.TotalAnnualOverhead.Should().Be(170_800m);
        result.OverheadRatePerLaborHour.Should().Be(16.42m);
    }

    [Fact]
    public async Task QuickStart_FullGlOff_SkipsGlWithNote()
    {
        var (db, _) = await SeedCostingAsync(withBook: true);
        var handler = new ApplyCostingQuickStartHandler(
            db, CapturingMediator([]).Object, new FakeCapabilities(false));

        var result = await handler.Handle(new ApplyCostingQuickStartCommand(Answers()), CancellationToken.None);

        result.GlBudgetsCreated.Should().BeFalse();
        result.Notes.Should().ContainMatch("*CAP-ACCT-FULLGL is off*");
        (await db.AcctBudgets.AnyAsync()).Should().BeFalse();
        (await db.GlAccounts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task QuickStart_ReApply_UpdatesGlBudgetsInPlace()
    {
        var (db, _) = await SeedCostingAsync(withBook: true);
        var handler = new ApplyCostingQuickStartHandler(db, CapturingMediator([]).Object, new FakeCapabilities(true));

        await handler.Handle(new ApplyCostingQuickStartCommand(Answers()), CancellationToken.None);
        var second = Answers() with { UtilitiesMonthly = 3_000m };
        await handler.Handle(new ApplyCostingQuickStartCommand(second), CancellationToken.None);

        var budgets = await db.AcctBudgets.Include(b => b.GlAccount).ToListAsync();
        budgets.Should().HaveCount(4, "re-apply upserts, never duplicates");
        budgets.Single(b => b.GlAccount!.AccountNumber == "60100").Amount.Should().Be(36_000m);
    }

    [Fact]
    public async Task QuickStart_ZeroAmountPool_IsSkippedWithNote()
    {
        var (db, _) = await SeedCostingAsync(withBook: true);
        var captured = new List<UpsertOverheadPoolBudgetCommand>();
        var handler = new ApplyCostingQuickStartHandler(db, CapturingMediator(captured).Object, new FakeCapabilities(true));

        var result = await handler.Handle(
            new ApplyCostingQuickStartCommand(Answers(equipment: 0m)), CancellationToken.None);

        captured.Should().HaveCount(3);
        result.PoolsConfigured.Should().NotContain("EQUIP");
        result.Notes.Should().ContainMatch("*Equipment*skipped*");
        (await db.AcctBudgets.CountAsync()).Should().Be(3);
    }
}
