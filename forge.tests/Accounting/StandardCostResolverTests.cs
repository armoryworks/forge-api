using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
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

        var elements = await new StandardCostResolver(db).ResolveAsync(part.Id);

        elements.Material.Should().Be(18m);
        elements.Labor.Should().Be(8m);
        elements.Overhead.Should().Be(4m);
        elements.Total.Should().Be(30m);
    }

    [Fact]
    public async Task RoutingRollup_LaborOverheadFromOperations_MaterialIsResidual()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 50m); // blended standard 50
        db.AddRange(
            new Operation { PartId = partId, StepNumber = 1, Title = "Mill", EstimatedLaborCost = 12m, EstimatedBurdenCost = 8m },
            new Operation { PartId = partId, StepNumber = 2, Title = "Finish", EstimatedLaborCost = 6m, EstimatedBurdenCost = 4m });
        await db.SaveChangesAsync();

        var elements = await new StandardCostResolver(db).ResolveAsync(partId);

        elements.Labor.Should().Be(18m);     // 12 + 6
        elements.Overhead.Should().Be(12m);  // 8 + 4
        elements.Material.Should().Be(20m);  // 50 − 18 − 12 (residual)
        elements.Total.Should().Be(50m, "elements reconcile to the blended standard");
    }

    [Fact]
    public async Task BlendedOnly_NoRouting_IsAllMaterial()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 25m);

        var elements = await new StandardCostResolver(db).ResolveAsync(partId);

        elements.Material.Should().Be(25m);
        elements.Labor.Should().Be(0m);
        elements.Overhead.Should().Be(0m);
    }

    [Fact]
    public async Task RoutingConversionExceedsBlended_MaterialFloorsAtZero()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db, manualOverride: 15m); // below routing conversion of 30
        db.Add(new Operation { PartId = partId, StepNumber = 1, Title = "Op", EstimatedLaborCost = 18m, EstimatedBurdenCost = 12m });
        await db.SaveChangesAsync();

        var elements = await new StandardCostResolver(db).ResolveAsync(partId);

        elements.Material.Should().Be(0m, "a blended total below routing conversion implies no material");
        elements.Labor.Should().Be(18m);
        elements.Overhead.Should().Be(12m);
    }
}
