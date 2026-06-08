using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// RecalculatePartStandardCost — runs the rollup, reconciles to the manual override, and freezes the result as
/// a current CostCalculation snapshot (with Inputs). The resolver then reads the frozen snapshot.
/// </summary>
public class RecalculatePartStandardCostHandlerTests
{
    private static RecalculatePartStandardCostHandler Handler(AppDbContext db)
        => new(db, new StandardCostRollupService(db), new SystemClock());

    [Fact]
    public async Task Recalculate_PersistsDecomposition_AsCurrentSnapshot_ResolverReadsIt()
    {
        using var db = TestDbContextFactory.Create();
        // Override 13; routing 1 hr @ (labor 2, burden 1) → labor 2, overhead 1, material residual 10.
        var part = new Part { PartNumber = "P-RC", Name = "x", ManualCostOverride = 13m };
        db.Add(part);
        var wc = new WorkCenter { Name = "WC", Code = "WC-RC", LaborCostPerHour = 2m, BurdenRatePerHour = 1m, IsActive = true };
        db.Add(wc);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", EstimatedMinutes = 60, WorkCenterId = wc.Id });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new RecalculatePartStandardCostCommand(part.Id), default);

        result.Material.Should().Be(10m);
        result.Labor.Should().Be(2m);
        result.Overhead.Should().Be(1m);
        result.Total.Should().Be(13m);

        db.ChangeTracker.Clear();
        var saved = await db.Parts.Include(p => p.CurrentCostCalculation).ThenInclude(c => c!.Inputs)
            .FirstAsync(p => p.Id == part.Id);
        saved.CurrentCostCalculationId.Should().Be(result.CostCalculationId);
        saved.CurrentCostCalculation!.IsCurrent.Should().BeTrue();
        saved.CurrentCostCalculation.ResultAmount.Should().Be(13m);
        saved.CurrentCostCalculation.Inputs!.DirectMaterialCost.Should().Be(10m);
        saved.CurrentCostCalculation.Inputs.DirectLaborCost.Should().Be(2m);
        saved.CurrentCostCalculation.Inputs.OverheadAmount.Should().Be(1m);

        // The resolver now returns the frozen snapshot (priority #1), not a fresh recompute.
        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db)).ResolveAsync(part.Id);
        elements.Material.Should().Be(10m);
        elements.Labor.Should().Be(2m);
        elements.Overhead.Should().Be(1m);
    }

    [Fact]
    public async Task Recalculate_Twice_SupersedesPriorSnapshot()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-RC2", Name = "x", ManualCostOverride = 5m };
        db.Add(part);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new RecalculatePartStandardCostCommand(part.Id), default);
        await Handler(db).Handle(new RecalculatePartStandardCostCommand(part.Id), default);

        (await db.Set<CostCalculation>().CountAsync(c => c.EntityType == "Part" && c.EntityId == part.Id && c.IsCurrent))
            .Should().Be(1, "exactly one snapshot is current");
        (await db.Set<CostCalculation>().CountAsync(c => c.EntityType == "Part" && c.EntityId == part.Id))
            .Should().Be(2, "the prior snapshot is retained, superseded");
    }
}
