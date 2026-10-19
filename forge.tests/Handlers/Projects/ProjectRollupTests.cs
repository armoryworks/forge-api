using FluentAssertions;

using Forge.Api.Features.Projects;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Projects;

public class ProjectRollupTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<(Project Project, WbsElement Element)> SeedRefurbishmentAsync(AppDbContext db, decimal? percentComplete = null)
    {
        var project = new Project
        {
            ProjectNumber = "PRJ-0001", Name = "Mold refurbishment", BudgetTotal = 18_500m,
            PlannedStartDate = Today.AddDays(-10), PlannedEndDate = Today.AddDays(10),
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var element = new WbsElement
        {
            ProjectId = project.Id, Code = "1.1", Name = "Cavity rework", Type = WbsElementType.WorkPackage,
            BudgetLabor = 6_000m, BudgetMaterial = 4_000m, BudgetTotal = 10_000m, PercentComplete = percentComplete,
        };
        db.WbsElements.Add(element);
        await db.SaveChangesAsync();

        await new AddWbsCostEntryHandler(db, new SystemClock()).Handle(
            new AddWbsCostEntryCommand(project.Id, element.Id, new CreateWbsCostEntryRequestModel { Category = WbsCostCategory.Material, Amount = 1_250m }),
            CancellationToken.None);

        return (project, element);
    }

    [Fact]
    public async Task Summary_reflects_recorded_cost_against_the_budget()
    {
        using var db = TestDbContextFactory.Create();
        var (project, element) = await SeedRefurbishmentAsync(db);

        var summary = await new GetProjectSummaryHandler(db, new SystemClock())
            .Handle(new GetProjectSummaryQuery(project.Id), CancellationToken.None);

        summary.ProjectId.Should().Be(project.Id);
        summary.BudgetTotal.Should().Be(18_500m);
        summary.ActualTotal.Should().Be(1_250m);
        summary.EstimateAtCompletion.Should().Be(19_750m);
        summary.VarianceAtCompletion.Should().Be(-1_250m);
        var node = summary.WbsTree.Should().ContainSingle().Subject;
        node.Id.Should().Be(element.Id);
        node.BudgetTotal.Should().Be(10_000m);
        node.ActualTotal.Should().Be(1_250m);
        node.Variance.Should().Be(8_750m);
    }

    [Fact]
    public async Task Earned_value_uses_percent_complete_and_the_planned_window()
    {
        using var db = TestDbContextFactory.Create();
        var (project, _) = await SeedRefurbishmentAsync(db, percentComplete: 25m);

        var ev = await new GetEarnedValueMetricsHandler(db, new SystemClock())
            .Handle(new GetEarnedValueMetricsQuery(project.Id), CancellationToken.None);

        ev.BudgetedCostOfWorkPerformed.Should().Be(4_625m);
        ev.BudgetedCostOfWorkScheduled.Should().Be(9_250m);
        ev.ActualCostOfWorkPerformed.Should().Be(1_250m);
        ev.CostVariance.Should().Be(3_375m);
        ev.ScheduleVariance.Should().Be(-4_625m);
        ev.CostPerformanceIndex.Should().Be(3.7m);
        ev.SchedulePerformanceIndex.Should().Be(0.5m);
        ev.EstimateAtCompletion.Should().Be(5_000m);
        ev.EstimateToComplete.Should().Be(3_750m);
    }

    [Fact]
    public async Task Parent_elements_roll_up_their_children()
    {
        using var db = TestDbContextFactory.Create();
        var project = new Project { ProjectNumber = "PRJ-0002", Name = "Line move" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var phase = new WbsElement { ProjectId = project.Id, Code = "1", Name = "Phase 1", Type = WbsElementType.Phase };
        db.WbsElements.Add(phase);
        await db.SaveChangesAsync();
        var a = new WbsElement { ProjectId = project.Id, ParentElementId = phase.Id, Code = "1.1", Name = "Rigging", BudgetTotal = 1_000m, PercentComplete = 50m, SortOrder = 1 };
        var b = new WbsElement { ProjectId = project.Id, ParentElementId = phase.Id, Code = "1.2", Name = "Wiring", BudgetTotal = 3_000m, PercentComplete = 0m, SortOrder = 2 };
        db.WbsElements.AddRange(a, b);
        db.WbsCostEntries.Add(new WbsCostEntry { WbsElement = a, Category = WbsCostCategory.Labor, Amount = 400m, EntryDate = DateTimeOffset.UtcNow });
        db.WbsCostEntries.Add(new WbsCostEntry { WbsElement = b, Category = WbsCostCategory.Subcontract, Amount = 100m, EntryDate = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var summary = await new GetProjectSummaryHandler(db, new SystemClock())
            .Handle(new GetProjectSummaryQuery(project.Id), CancellationToken.None);

        summary.BudgetTotal.Should().Be(4_000m);
        summary.ActualTotal.Should().Be(500m);
        summary.PercentComplete.Should().Be(12.5m);
        var root = summary.WbsTree.Should().ContainSingle().Subject;
        root.Code.Should().Be("1");
        root.BudgetTotal.Should().Be(4_000m);
        root.ActualTotal.Should().Be(500m);
        root.PercentComplete.Should().Be(12.5m);
        root.Children.Select(c => c.Code).Should().Equal("1.1", "1.2");
    }

    [Fact]
    public async Task Recalculate_writes_actuals_from_the_cost_entries()
    {
        using var db = TestDbContextFactory.Create();
        var (project, element) = await SeedRefurbishmentAsync(db);
        element.ActualMaterial = 0m;
        element.ActualTotal = 0m;
        await db.SaveChangesAsync();

        await new RecalculateProjectTotalsHandler(db, new SystemClock())
            .Handle(new RecalculateProjectTotalsCommand(project.Id), CancellationToken.None);

        element.ActualMaterial.Should().Be(1_250m);
        element.ActualTotal.Should().Be(1_250m);
        project.ActualTotal.Should().Be(1_250m);
        project.EstimateAtCompletionTotal.Should().Be(19_750m);
    }

    [Fact]
    public async Task Unknown_project_is_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new GetProjectSummaryHandler(db, new SystemClock())
            .Handle(new GetProjectSummaryQuery(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
