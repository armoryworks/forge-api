using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// StandardCostResolver — decomposes a part's standard unit cost into material/labor/overhead, reconciling to
/// the blended standard. Priority: explicit CostCalculation.Inputs → routing rollup (material = residual) →
/// blended ManualCostOverride as material-only.
/// </summary>
public class StandardCostResolverTests
{
    private static async Task<int> AddPartAsync(AppDbContext db, decimal? manualOverride)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "x", ManualCostOverride = manualOverride };
        db.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    [Fact]
    public async Task ExplicitCostCalcInputs_AreUsedDirectly()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-CC", Name = "x" };
        db.Add(part);
        await db.SaveChangesAsync();

        var calc = new CostCalculation
        {
            EntityType = "Part", EntityId = part.Id, ResultAmount = 30m, IsCurrent = true,
            Inputs = new CostCalculationInputs { DirectMaterialCost = 18m, DirectLaborCost = 8m, OverheadAmount = 4m },
        };
        db.Add(calc);
        await db.SaveChangesAsync();
        part.CurrentCostCalculationId = calc.Id;
        await db.SaveChangesAsync();

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(part.Id);

        elements.Material.Should().Be(18m);
        elements.Labor.Should().Be(8m);
        elements.Overhead.Should().Be(4m);
        elements.Total.Should().Be(30m);
    }

    [Fact]
    public async Task RoutingRollup_LaborOverheadFromWorkCenterRates_MaterialIsResidual()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 50m); // carried standard 50
        var wc = new WorkCenter { Name = "Mill", Code = "MILL", LaborCostPerHour = 18m, BurdenRatePerHour = 12m, IsActive = true };
        db.Add(wc);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = partId, StepNumber = 1, Title = "Mill", EstimatedMs = 3_600_000L, WorkCenterId = wc.Id });
        await db.SaveChangesAsync();

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(partId);

        elements.Labor.Should().Be(18m);     // 1 hr × 18
        elements.Overhead.Should().Be(12m);  // 1 hr × 12
        elements.Material.Should().Be(20m);  // 50 − 18 − 12 (residual)
        elements.Total.Should().Be(50m, "elements reconcile to the carried standard");
    }

    [Fact]
    public async Task BlendedOnly_NoRouting_IsAllMaterial()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 25m);

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(partId);

        elements.Material.Should().Be(25m);
        elements.Labor.Should().Be(0m);
        elements.Overhead.Should().Be(0m);
    }

    [Fact]
    public async Task RoutingConversionExceedsOverride_ScalesConversion_MaterialZero()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 15m); // below routing conversion of 30
        var wc = new WorkCenter { Name = "Op", Code = "OP1", LaborCostPerHour = 18m, BurdenRatePerHour = 12m, IsActive = true };
        db.Add(wc);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = partId, StepNumber = 1, Title = "Op", EstimatedMs = 3_600_000L, WorkCenterId = wc.Id });
        await db.SaveChangesAsync();

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(partId);

        // Conversion 30 exceeds the override 15 → scaled to fit, no implied material; total stays at 15.
        elements.Material.Should().Be(0m);
        elements.Labor.Should().Be(9m);     // 15 × (18/30)
        elements.Overhead.Should().Be(6m);  // 15 − 9
        elements.Total.Should().Be(15m);
    }

    private static async Task<Part> AddPartWithCalculationAsync(AppDbContext db, decimal? manualOverride)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "x", ManualCostOverride = manualOverride };
        db.Add(part);
        await db.SaveChangesAsync();
        var calc = new CostCalculation
        {
            EntityType = "Part", EntityId = part.Id, ResultAmount = 30m, IsCurrent = true,
            Inputs = new CostCalculationInputs { DirectMaterialCost = 18m, DirectLaborCost = 8m, OverheadAmount = 4m },
        };
        db.Add(calc);
        await db.SaveChangesAsync();
        part.CurrentCostCalculationId = calc.Id;
        await db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task ManualOverride_IsTheCostOfRecord_OverACalculationBreakdown()
    {
        using var db = TestDbContextFactory.Create();
        var part = await AddPartWithCalculationAsync(db, manualOverride: 40m);

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(part.Id);

        elements.Total.Should().Be(40m, "the manual override is the cost of record, not the calculation's 30");
        elements.Labor.Should().Be(8m);
        elements.Overhead.Should().Be(4m);
        elements.Material.Should().Be(28m);
    }

    [Fact]
    public async Task Resolver_AndPartDetail_AgreeOnTheStandard()
    {
        using var db = TestDbContextFactory.Create();
        var part = await AddPartWithCalculationAsync(db, manualOverride: 40m);
        db.ChangeTracker.Clear();
        var loaded = await db.Parts.Include(p => p.CurrentCostCalculation).ThenInclude(c => c!.Inputs).SingleAsync(p => p.Id == part.Id);

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(part.Id);
        var model = PartStandardCostModel.From(loaded)!;

        elements.Total.Should().Be(model.Amount);
        elements.Material.Should().Be(model.MaterialCost);
        elements.Labor.Should().Be(model.LaborCost);
        elements.Overhead.Should().Be(model.OverheadCost);
    }

    [Fact]
    public async Task ZeroOverride_ResolvesToZero()
    {
        using var db = TestDbContextFactory.Create();
        var part = await AddPartWithCalculationAsync(db, manualOverride: 0m);

        var elements = await new StandardCostResolver(db, new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off)).ResolveAsync(part.Id);

        elements.Should().Be(StandardCostElements.Zero);
    }
}
