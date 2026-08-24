using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Costing.Tier3;
using Forge.Core.Entities.Accounting;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

/// <summary>
/// User-defined costing templates: save/replace/delete lifecycle (system templates
/// never deletable), and apply — each line's answer annualized per its basis into
/// an overhead-pool budget, GL mirroring for lines that name an account, default
/// fallback for unanswered lines, idempotent re-apply.
/// </summary>
public class CostingTemplateTests
{
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; } = DateTimeOffset.UnixEpoch; }

    private static async Task<(AppDbContext Db, CostingTemplate Template)> SeedAsync(bool withBook = true)
    {
        var db = TestDbContextFactory.Create();
        var center = new CostingCostCenter { Code = "PLANT", Name = "Plant", Type = CostCenterType.Production };
        db.CostingCostCenters.Add(center);
        db.CostingPeriods.Add(new CostingPeriod
        {
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        });

        var template = new CostingTemplate
        {
            Name = "Test package",
            IsSystem = true,
            Lines =
            [
                new CostingTemplateLine { Code = "UTIL", Name = "Utilities", Behavior = OverheadBehavior.Variable, Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.MonthlyAmount, GlAccountNumber = "60100", GlAccountName = "Utilities", SortOrder = 0 },
                new CostingTemplateLine { Code = "TAX", Name = "Payroll Taxes", Behavior = OverheadBehavior.Variable, Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.PercentOfWages, DefaultValue = 8m, GlAccountNumber = "60300", GlAccountName = "Payroll Tax Expense", SortOrder = 1 },
                new CostingTemplateLine { Code = "BEN", Name = "Benefits", Behavior = OverheadBehavior.Variable, Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.MonthlyPerEmployee, GlAccountNumber = "60400", GlAccountName = "Benefits", SortOrder = 2 },
                new CostingTemplateLine { Code = "EQUIP", Name = "Equipment", Behavior = OverheadBehavior.Fixed, Driver = OverheadDriver.LaborHour, AmountBasis = CostingAmountBasis.AnnualAmount, SortOrder = 3 },
            ],
        };
        db.CostingTemplates.Add(template);

        foreach (var code in new[] { "UTIL", "TAX", "BEN", "EQUIP" })
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
        return (db, template);
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

    private static ApplyCostingTemplateRequestModel Answers(Dictionary<string, decimal>? values = null) => new()
    {
        FiscalYear = 2026,
        DirectHeadcount = 5m,
        AverageHourlyWage = 25m,
        // TAX intentionally unanswered — falls back to the line default (8%).
        Values = values ?? new Dictionary<string, decimal>
        {
            ["UTIL"] = 2_000m,     // monthly → 24,000
            ["BEN"] = 500m,        // per employee per month → 30,000
            ["EQUIP"] = 24_000m,   // annual
        },
    };

    [Fact]
    public async Task Apply_AnnualizesEachBasis_AndMirrorsGl()
    {
        var (db, template) = await SeedAsync();
        var captured = new List<UpsertOverheadPoolBudgetCommand>();
        var handler = new ApplyCostingTemplateHandler(db, CapturingMediator(captured).Object, new FakeCapabilities(true));

        var result = await handler.Handle(new ApplyCostingTemplateCommand(template.Id, Answers()), CancellationToken.None);

        // 5 × 2080 = 10,400 hours; wages 260,000; TAX default 8% → 20,800.
        result.AnnualDirectLaborHours.Should().Be(10_400m);
        captured.Should().HaveCount(4);
        captured.Select(c => c.BudgetAmount).Should().BeEquivalentTo(new[] { 24_000m, 20_800m, 30_000m, 24_000m });
        captured.Should().OnlyContain(c => c.BudgetDriverQty == 10_400m);

        // GL: only lines that name an account mirror (EQUIP has none).
        result.GlBudgetsCreated.Should().BeTrue();
        var budgets = await db.AcctBudgets.Include(b => b.GlAccount).ToListAsync();
        budgets.Should().HaveCount(3);
        budgets.Select(b => b.GlAccount!.AccountNumber).Should().BeEquivalentTo(["60100", "60300", "60400"]);

        result.TotalAnnualOverhead.Should().Be(98_800m);
        result.OverheadRatePerLaborHour.Should().Be(9.50m);
    }

    [Fact]
    public async Task Apply_FullGlOff_SkipsGlWithNote()
    {
        var (db, template) = await SeedAsync();
        var handler = new ApplyCostingTemplateHandler(db, CapturingMediator([]).Object, new FakeCapabilities(false));

        var result = await handler.Handle(new ApplyCostingTemplateCommand(template.Id, Answers()), CancellationToken.None);

        result.GlBudgetsCreated.Should().BeFalse();
        result.Notes.Should().ContainMatch("*CAP-ACCT-FULLGL is off*");
        (await db.AcctBudgets.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_ReApply_UpsertsGlInPlace()
    {
        var (db, template) = await SeedAsync();
        var handler = new ApplyCostingTemplateHandler(db, CapturingMediator([]).Object, new FakeCapabilities(true));

        await handler.Handle(new ApplyCostingTemplateCommand(template.Id, Answers()), CancellationToken.None);
        var second = Answers(new Dictionary<string, decimal> { ["UTIL"] = 3_000m, ["BEN"] = 500m, ["EQUIP"] = 24_000m });
        await handler.Handle(new ApplyCostingTemplateCommand(template.Id, second), CancellationToken.None);

        var budgets = await db.AcctBudgets.Include(b => b.GlAccount).ToListAsync();
        budgets.Should().HaveCount(3, "re-apply upserts, never duplicates");
        budgets.Single(b => b.GlAccount!.AccountNumber == "60100").Amount.Should().Be(36_000m);
    }

    [Fact]
    public async Task Save_CreatesAndReplacesWholeGraph()
    {
        var db = TestDbContextFactory.Create();
        var handler = new SaveCostingTemplateHandler(db);

        var created = await handler.Handle(new SaveCostingTemplateCommand(new SaveCostingTemplateRequestModel
        {
            Name = "My shop",
            Lines =
            [
                new SaveCostingTemplateLineModel("util", "Utilities", OverheadBehavior.Variable, OverheadDriver.LaborHour, CostingAmountBasis.MonthlyAmount, null, "60100", "Utilities"),
                new SaveCostingTemplateLineModel("INS", "Insurance", OverheadBehavior.Fixed, OverheadDriver.LaborHour, CostingAmountBasis.AnnualAmount, null, null, null),
            ],
        }), CancellationToken.None);

        created.Lines.Should().HaveCount(2);
        created.Lines[0].Code.Should().Be("UTIL", "codes normalize to uppercase");

        var updated = await handler.Handle(new SaveCostingTemplateCommand(new SaveCostingTemplateRequestModel
        {
            Id = created.Id,
            Name = "My shop v2",
            Lines = [new SaveCostingTemplateLineModel("RENT", "Rent", OverheadBehavior.Fixed, OverheadDriver.LaborHour, CostingAmountBasis.MonthlyAmount, null, "60200", "Rent")],
        }), CancellationToken.None);

        updated.Name.Should().Be("My shop v2");
        updated.Lines.Should().HaveCount(1, "save replaces the whole line graph");
        (await db.CostingTemplateLines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_RefusesSystem_AllowsUserTemplate()
    {
        var (db, system) = await SeedAsync(withBook: false);
        var user = new CostingTemplate { Name = "Mine", Lines = [new CostingTemplateLine { Code = "X", Name = "X" }] };
        db.CostingTemplates.Add(user);
        await db.SaveChangesAsync();
        var handler = new DeleteCostingTemplateHandler(db, new FixedClock());

        var act = () => handler.Handle(new DeleteCostingTemplateCommand(system.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*System*");

        await handler.Handle(new DeleteCostingTemplateCommand(user.Id), CancellationToken.None);
        (await db.CostingTemplates.CountAsync(t => t.Name == "Mine")).Should().Be(0, "soft-deleted rows drop out of the filtered set");
    }
}
