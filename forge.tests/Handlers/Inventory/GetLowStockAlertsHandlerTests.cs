using FluentAssertions;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class GetLowStockAlertsHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<Part> SeedAsync(string partNumber, decimal stock, decimal? minStock, decimal? reorderPoint)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            Status = PartStatus.Active,
            MinStockThreshold = minStock,
            ReorderPoint = reorderPoint,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        _db.BinContents.Add(new BinContent { EntityType = "part", EntityId = part.Id, Quantity = stock, LocationId = 1 });
        await _db.SaveChangesAsync();
        return part;
    }

    private Task<List<Forge.Core.Models.LowStockAlertModel>> AlertsAsync() =>
        new GetLowStockAlertsHandler(_db).Handle(new GetLowStockAlertsQuery(), CancellationToken.None);

    [Fact]
    public async Task Part_with_only_a_reorder_point_alerts_at_or_below_it()
    {
        var below = await SeedAsync("RP-BELOW", stock: 900m, minStock: null, reorderPoint: 1100m);
        var at = await SeedAsync("RP-AT", stock: 1100m, minStock: null, reorderPoint: 1100m);
        await SeedAsync("RP-ABOVE", stock: 1200m, minStock: null, reorderPoint: 1100m);

        var alerts = await AlertsAsync();

        alerts.Select(a => a.PartId).Should().BeEquivalentTo(new[] { below.Id, at.Id });
        var alert = alerts.Single(a => a.PartId == below.Id);
        alert.MinStockThreshold.Should().BeNull();
        alert.ReorderPoint.Should().Be(1100m);
    }

    [Fact]
    public async Task Minimum_stock_still_alerts_strictly_below_it()
    {
        var below = await SeedAsync("MIN-BELOW", stock: 4m, minStock: 5m, reorderPoint: null);
        await SeedAsync("MIN-AT", stock: 5m, minStock: 5m, reorderPoint: null);

        (await AlertsAsync()).Select(a => a.PartId).Should().Equal(below.Id);
    }

    [Fact]
    public async Task Part_without_either_level_never_alerts()
    {
        await SeedAsync("NO-LEVELS", stock: 0m, minStock: null, reorderPoint: null);

        (await AlertsAsync()).Should().BeEmpty();
    }
}
