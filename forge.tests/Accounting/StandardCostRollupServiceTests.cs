using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// StandardCostRollupService — computes a part's decomposed standard from master data: labor/overhead from
/// routing (EstimatedMinutes × work-center rates) and material from the recursive BOM (child standard × qty).
/// </summary>
public class StandardCostRollupServiceTests
{
    private static async Task<int> AddPartAsync(AppDbContext db, decimal? manualOverride = null)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "x", ManualCostOverride = manualOverride };
        db.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    private static async Task AddRoutingAsync(AppDbContext db, int partId, int minutes, decimal laborRate, decimal burdenRate)
    {
        var wc = new WorkCenter { Name = "WC", Code = $"WC-{Guid.NewGuid():N}", LaborCostPerHour = laborRate, BurdenRatePerHour = burdenRate, IsActive = true };
        db.Add(wc);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = partId, StepNumber = 1, Title = "Op", EstimatedMinutes = minutes, WorkCenterId = wc.Id });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollup_LaborOverheadFromRouting()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db);
        await AddRoutingAsync(db, partId, minutes: 90, laborRate: 20m, burdenRate: 8m); // 1.5 hr

        var e = await new StandardCostRollupService(db).RollupAsync(partId);

        e.Labor.Should().Be(30m);    // 1.5 × 20
        e.Overhead.Should().Be(12m); // 1.5 × 8
        e.Material.Should().Be(0m, "no BOM components");
    }

    [Fact]
    public async Task Rollup_MaterialFromRecursiveBom()
    {
        using var db = TestDbContextFactory.Create();
        var child = await AddPartAsync(db, manualOverride: 10m); // purchased leaf — standard 10
        var parent = await AddPartAsync(db);
        await AddRoutingAsync(db, parent, minutes: 60, laborRate: 5m, burdenRate: 3m); // 1 hr → labor 5, oh 3
        db.Add(new BOMLine { ParentPartId = parent, ChildPartId = child, Quantity = 2m });
        await db.SaveChangesAsync();

        var e = await new StandardCostRollupService(db).RollupAsync(parent);

        e.Material.Should().Be(20m);  // 2 × 10 (child standard)
        e.Labor.Should().Be(5m);
        e.Overhead.Should().Be(3m);
        e.Total.Should().Be(28m);
    }

    [Fact]
    public async Task Rollup_MultiLevelBom_RollsChildConversionIntoParentMaterial()
    {
        using var db = TestDbContextFactory.Create();
        // Sub-assembly with its own routing (labor 4) + a purchased leaf (5); parent consumes 1 of the sub.
        var leaf = await AddPartAsync(db, manualOverride: 5m);
        var sub = await AddPartAsync(db);
        await AddRoutingAsync(db, sub, minutes: 60, laborRate: 4m, burdenRate: 0m); // sub labor 4
        db.Add(new BOMLine { ParentPartId = sub, ChildPartId = leaf, Quantity = 1m });
        await db.SaveChangesAsync();

        var parent = await AddPartAsync(db);
        db.Add(new BOMLine { ParentPartId = parent, ChildPartId = sub, Quantity = 1m });
        await db.SaveChangesAsync();

        var e = await new StandardCostRollupService(db).RollupAsync(parent);

        // Sub standard = material 5 (leaf) + labor 4 = 9; parent material = 1 × 9.
        e.Material.Should().Be(9m);
    }

    [Fact]
    public async Task Rollup_BomCycle_IsGuarded()
    {
        using var db = TestDbContextFactory.Create();
        var a = await AddPartAsync(db, manualOverride: 1m);
        var b = await AddPartAsync(db, manualOverride: 1m);
        db.Add(new BOMLine { ParentPartId = a, ChildPartId = b, Quantity = 1m });
        db.Add(new BOMLine { ParentPartId = b, ChildPartId = a, Quantity = 1m }); // cycle
        await db.SaveChangesAsync();

        // Must terminate (cycle guard) rather than recurse forever.
        var e = await new StandardCostRollupService(db).RollupAsync(a);
        e.Should().NotBeNull();
    }
}
