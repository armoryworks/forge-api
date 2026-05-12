using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Data;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Pillar 2 follow-up — covers the idempotent seed for the
/// <c>part.material_spec</c> (hierarchical) and <c>part.valuation_class</c>
/// (flat) reference_data groups whose Part FK columns shipped empty in
/// commit aae9e54.
/// </summary>
public class SeedPartRefDataTests
{
    private const string MaterialSpecGroup = "part.material_spec";
    private const string ValuationClassGroup = "part.valuation_class";

    [Fact]
    public async Task SeedPartMaterialSpecs_InsertsParentsAndChildren()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedPartMaterialSpecsAsync(db);

        var rows = await db.ReferenceData
            .Where(r => r.GroupCode == MaterialSpecGroup)
            .ToListAsync();

        // 10 parent families + 28 grade children = 38 rows.
        rows.Should().HaveCount(38);

        var parents = rows.Where(r => r.ParentId == null).ToList();
        parents.Should().HaveCount(10);
        parents.Select(p => p.Code).Should().BeEquivalentTo(new[]
        {
            "aluminum", "steel", "stainless", "plastic", "brass",
            "copper", "titanium", "other-metal", "composite", "rubber",
        });

        // Hierarchy intact — every child points at a row in the same group.
        var parentIds = parents.Select(p => p.Id).ToHashSet();
        var children = rows.Where(r => r.ParentId != null).ToList();
        children.Should().HaveCount(28);
        children.Should().OnlyContain(c => parentIds.Contains(c.ParentId!.Value));

        // Spot-check a child resolves to the correct parent family.
        var aluminumId = parents.Single(p => p.Code == "aluminum").Id;
        var alGrade = rows.Single(r => r.Code == "aluminum-6061-t6");
        alGrade.ParentId.Should().Be(aluminumId);
        alGrade.Label.Should().Be("6061-T6");

        var stainlessId = parents.Single(p => p.Code == "stainless").Id;
        rows.Single(r => r.Code == "stainless-17-4-ph").ParentId.Should().Be(stainlessId);

        // Every seeded row is flagged + active.
        rows.Should().OnlyContain(r => r.IsSeedData && r.IsActive);
    }

    [Fact]
    public async Task SeedPartMaterialSpecs_IsIdempotent()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedPartMaterialSpecsAsync(db);
        var firstCount = await db.ReferenceData.CountAsync(r => r.GroupCode == MaterialSpecGroup);

        await SeedData.SeedPartMaterialSpecsAsync(db);
        var secondCount = await db.ReferenceData.CountAsync(r => r.GroupCode == MaterialSpecGroup);

        secondCount.Should().Be(firstCount, "second seed run must insert nothing new");
        secondCount.Should().Be(38);
    }

    [Fact]
    public async Task SeedPartValuationClasses_InsertsAllRows()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedPartValuationClassesAsync(db);

        var rows = await db.ReferenceData
            .Where(r => r.GroupCode == ValuationClassGroup)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        rows.Should().HaveCount(5);
        rows.Select(r => r.Code).Should().Equal("fifo", "lifo", "weighted-avg", "standard", "specific");
        rows.Should().OnlyContain(r => r.IsSeedData && r.IsActive && r.ParentId == null);
    }

    [Fact]
    public async Task SeedPartValuationClasses_IsIdempotent()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedPartValuationClassesAsync(db);
        var firstCount = await db.ReferenceData.CountAsync(r => r.GroupCode == ValuationClassGroup);

        await SeedData.SeedPartValuationClassesAsync(db);
        var secondCount = await db.ReferenceData.CountAsync(r => r.GroupCode == ValuationClassGroup);

        secondCount.Should().Be(firstCount, "second seed run must insert nothing new");
        secondCount.Should().Be(5);
    }
}
