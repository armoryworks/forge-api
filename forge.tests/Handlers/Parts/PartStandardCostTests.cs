using FluentAssertions;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class PartStandardCostTests
{
    private static Part NewPart(decimal? manualOverride = null) => new()
    {
        PartNumber = $"P-{Guid.NewGuid():N}",
        Name = "Widget",
        ProcurementSource = ProcurementSource.Make,
        InventoryClass = InventoryClass.FinishedGood,
        Status = PartStatus.Active,
        ManualCostOverride = manualOverride,
    };

    private static CostCalculation NewCalc(int partId, decimal amount) => new()
    {
        EntityType = "Part",
        EntityId = partId,
        ProfileId = 1,
        ResultAmount = amount,
        CalculatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        IsCurrent = true,
        Inputs = new CostCalculationInputs { DirectMaterialCost = 6m, DirectLaborCost = 3m, OverheadAmount = 1.5m },
    };

    [Fact]
    public void From_ReturnsNull_WhenNeitherOverrideNorCalculation()
    {
        PartStandardCostModel.From(NewPart()).Should().BeNull();
    }

    [Fact]
    public void From_UsesTheCalculation_WhenNoOverride()
    {
        var part = NewPart();
        part.CurrentCostCalculation = NewCalc(1, 10.5m);

        var model = PartStandardCostModel.From(part)!;

        model.Amount.Should().Be(10.5m);
        model.Source.Should().Be(PartStandardCostModel.CalculationSource);
        model.MaterialCost.Should().Be(6m);
        model.LaborCost.Should().Be(3m);
        model.OverheadCost.Should().Be(1.5m);
    }

    [Fact]
    public void From_PrefersTheOverride_AndKeepsTheCalculationForContext()
    {
        var part = NewPart(manualOverride: 12m);
        part.CurrentCostCalculation = NewCalc(1, 10.5m);

        var model = PartStandardCostModel.From(part)!;

        model.Amount.Should().Be(12m);
        model.Source.Should().Be(PartStandardCostModel.OverrideSource);
        model.CalculatedAmount.Should().Be(10.5m);
    }

    [Fact]
    public void From_OverrideOnly_HasNoCalculationDetail()
    {
        var model = PartStandardCostModel.From(NewPart(manualOverride: 4m))!;

        model.Should().BeEquivalentTo(new PartStandardCostModel(4m, PartStandardCostModel.OverrideSource, null, null, null, null, null));
    }

    [Fact]
    public async Task GetDetailAsync_IncludesTheEffectiveStandardCost()
    {
        using var db = TestDbContextFactory.Create();
        var part = NewPart();
        db.Add(part);
        await db.SaveChangesAsync();
        var calc = NewCalc(part.Id, 10.5m);
        db.Add(calc);
        await db.SaveChangesAsync();
        part.CurrentCostCalculationId = calc.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var detail = await NewRepo(db).GetDetailAsync(part.Id, CancellationToken.None);

        detail!.StandardCost.Should().NotBeNull();
        detail.StandardCost!.Amount.Should().Be(10.5m);
        detail.StandardCost.Source.Should().Be(PartStandardCostModel.CalculationSource);
        detail.StandardCost.MaterialCost.Should().Be(6m);
    }

    private static PartRepository NewRepo(AppDbContext db)
    {
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(p => p.ResolveAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, int? _, decimal? _, CancellationToken _) => new ResolvedPartPrice(id, 0m, "USD", PartPriceSource.Default, null, null));
        return new PartRepository(db, pricing.Object);
    }
}
