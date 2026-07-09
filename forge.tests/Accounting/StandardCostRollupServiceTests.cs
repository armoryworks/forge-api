using System.Text.Json;

using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Integrations;
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

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off).RollupAsync(partId);

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

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off).RollupAsync(parent);

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

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off).RollupAsync(parent);

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
        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off).RollupAsync(a);
        e.Should().NotBeNull();
    }

    /// <summary>Seed one op on a fresh work center; returns the work-center id for departmental rate config.</summary>
    private static async Task<int> AddSingleOpWorkCenterAsync(AppDbContext db, int partId, int minutes, decimal laborRate, decimal burdenRate)
    {
        var wc = new WorkCenter { Name = "WC", Code = $"WC-{Guid.NewGuid():N}", LaborCostPerHour = laborRate, BurdenRatePerHour = burdenRate, IsActive = true };
        db.Add(wc);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = partId, StepNumber = 1, Title = "Op", EstimatedMinutes = minutes, WorkCenterId = wc.Id });
        await db.SaveChangesAsync();
        return wc.Id;
    }

    private static async Task AddDepartmentalProfileAsync(AppDbContext db, params (int WorkCenterId, decimal RatePct)[] rates)
    {
        var rows = rates.Select(r => new { work_center_id = r.WorkCenterId, rate_pct = r.RatePct }).ToArray();
        db.Add(new CostingProfile
        {
            Code = "default",
            Mode = "departmental",
            DepartmentalRates = JsonSerializer.Serialize(rows, StandardCostRollupService.DeptRateJson),
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        });
        await db.SaveChangesAsync();
    }

    [Fact] // Tier 2: departmental overhead = per-work-center % of the op's direct labor (not burden × hours).
    public async Task Rollup_DepartmentalMode_AppliesPctOfLabor()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db);
        var wcId = await AddSingleOpWorkCenterAsync(db, partId, minutes: 90, laborRate: 20m, burdenRate: 8m); // 1.5 hr
        await AddDepartmentalProfileAsync(db, (wcId, 150m)); // 150% of labor

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Tier2On).RollupAsync(partId);

        e.Labor.Should().Be(30m);    // 1.5 × 20
        e.Overhead.Should().Be(45m); // labor 30 × 150% — NOT the flat 1.5 × 8 = 12
    }

    [Fact] // Departmental mode: a work center with no configured rate falls back to its flat burden rate.
    public async Task Rollup_DepartmentalMode_FallsBackToBurden_WhenWorkCenterUnrated()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db);
        var wcId = await AddSingleOpWorkCenterAsync(db, partId, minutes: 90, laborRate: 20m, burdenRate: 8m);
        await AddDepartmentalProfileAsync(db, (wcId + 9999, 150m)); // rate is for some OTHER work center

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Tier2On).RollupAsync(partId);

        e.Overhead.Should().Be(12m); // fallback: 1.5 × 8
    }

    [Fact] // Capability gate: a departmental profile is ignored (flat rates) when CAP-COSTING-TIER2 is off.
    public async Task Rollup_DepartmentalProfile_IgnoredWhenCapabilityDisabled()
    {
        using var db = TestDbContextFactory.Create();
        var partId = await AddPartAsync(db);
        var wcId = await AddSingleOpWorkCenterAsync(db, partId, minutes: 90, laborRate: 20m, burdenRate: 8m);
        await AddDepartmentalProfileAsync(db, (wcId, 150m));

        var e = await new StandardCostRollupService(db, new SystemClock(), StubCapabilitySnapshotProvider.Off).RollupAsync(partId);

        e.Overhead.Should().Be(12m); // flat 1.5 × 8 — departmental config not applied without the capability
    }
}
