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

public class PartListPurchasableFilterTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly PartRepository _repo;

    public PartListPurchasableFilterTests()
    {
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(p => p.ResolveManyAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new ResolvedPartPrice(id, 0m, "USD", PartPriceSource.Default, null, null)));
        _repo = new PartRepository(_db, pricing.Object);
    }

    private async Task SeedAsync()
    {
        _db.Parts.AddRange(
            Part("MK-1", ProcurementSource.Make, PartStatus.Active),
            Part("BY-1", ProcurementSource.Buy, PartStatus.Active),
            Part("SC-1", ProcurementSource.Subcontract, PartStatus.Active),
            Part("PH-1", ProcurementSource.Phantom, PartStatus.Active),
            Part("BY-OLD", ProcurementSource.Buy, PartStatus.Obsolete));
        await _db.SaveChangesAsync();
    }

    private static Part Part(string number, ProcurementSource source, PartStatus status) => new()
    {
        PartNumber = number,
        Name = $"{number} name",
        ProcurementSource = source,
        InventoryClass = InventoryClass.Component,
        Status = status,
    };

    private async Task<List<string>> NumbersAsync(PartListQuery query)
    {
        var page = await _repo.GetPagedAsync(query with { Sort = "partNumber", Order = "asc" }, CancellationToken.None);
        return page.Items.Select(i => i.PartNumber).ToList();
    }

    [Fact]
    public async Task Purchasable_true_keeps_buy_and_subcontract_parts()
    {
        await SeedAsync();

        var numbers = await NumbersAsync(new PartListQuery { Purchasable = true });

        numbers.Should().Equal("BY-1", "BY-OLD", "SC-1");
    }

    [Fact]
    public async Task Purchasable_false_keeps_make_and_phantom_parts()
    {
        await SeedAsync();

        var numbers = await NumbersAsync(new PartListQuery { Purchasable = false });

        numbers.Should().Equal("MK-1", "PH-1");
    }

    [Fact]
    public async Task Purchasable_combines_with_the_active_filter_and_search()
    {
        await SeedAsync();

        var numbers = await NumbersAsync(new PartListQuery { Purchasable = true, IsActive = true, Q = "by" });

        numbers.Should().Equal("BY-1");
    }

    [Fact]
    public async Task Total_count_reflects_the_purchasable_filter_not_the_page()
    {
        await SeedAsync();

        var page = await _repo.GetPagedAsync(new PartListQuery { Purchasable = true, PageSize = 1 }, CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(1);
    }
}
