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

public class PartDetailBomLineTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<Part> SeedPartAsync(string partNumber, InventoryClass inventoryClass)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = inventoryClass,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task BomLines_FlagChildrenThatHaveTheirOwnBom()
    {
        var top = await SeedPartAsync("FG-400", InventoryClass.FinishedGood);
        var sub = await SeedPartAsync("SA-210", InventoryClass.Subassembly);
        var bar = await SeedPartAsync("RM-6061-B12", InventoryClass.Raw);
        _db.BOMLines.AddRange(
            new BOMLine { ParentPartId = top.Id, ChildPartId = sub.Id, Quantity = 2m, SortOrder = 1, SourceType = BOMSourceType.Make },
            new BOMLine { ParentPartId = top.Id, ChildPartId = bar.Id, Quantity = 1m, SortOrder = 2, SourceType = BOMSourceType.Buy },
            new BOMLine { ParentPartId = sub.Id, ChildPartId = bar.Id, Quantity = 0.5m, SortOrder = 1, SourceType = BOMSourceType.Buy });
        await _db.SaveChangesAsync();
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(r => r.ResolveAsync(It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int partId, int? _, decimal? _, CancellationToken _) =>
                new ResolvedPartPrice(partId, 0m, "USD", PartPriceSource.Default, null, null));
        var repo = new PartRepository(_db, pricing.Object);

        var detail = await repo.GetDetailAsync(top.Id, CancellationToken.None);

        detail!.BomLines.Select(l => (l.ChildPartNumber, l.ChildHasBom))
            .Should().Equal(("SA-210", true), ("RM-6061-B12", false));
    }
}
