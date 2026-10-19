using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class AbcClassificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();

    public AbcClassificationTests() => _clock.Setup(c => c.UtcNow).Returns(Now);

    [Fact]
    public async Task UsageValue_IsConsumptionAtCost_SoIssuedMaterialOutranksTheProduct()
    {
        var product = await AddPartAsync("FG-1", standardCost: 100m);
        var resin = await AddPartAsync("RESIN");
        await AddPurchaseCostAsync(resin, 5m);
        await AddShipmentAsync(product, 10m, Now.AddDays(-20));
        await AddIssueAsync(resin, 1000m, MaterialIssueType.Issue, Now.AddDays(-25));
        await AddIssueAsync(resin, 100m, MaterialIssueType.Return, Now.AddDays(-24));

        var results = await RunAsync();

        var resinRow = results.Single(r => r.PartId == resin.Id);
        resinRow.AnnualDemandQuantity.Should().Be(900m);
        resinRow.UnitCost.Should().Be(5m);
        resinRow.AnnualUsageValue.Should().Be(4500m);
        resinRow.Rank.Should().Be(1);
        resinRow.Classification.Should().Be(AbcClass.A);
        results.Single(r => r.PartId == product.Id).AnnualUsageValue.Should().Be(1000m);
    }

    [Fact]
    public async Task ComponentWithoutHistory_IsValuedOnTheExplodedOpenDemand()
    {
        var product = await AddPartAsync("FG-2", standardCost: 50m);
        var bar = await AddPartAsync("BAR");
        var unused = await AddPartAsync("UNUSED");
        await AddPurchaseCostAsync(bar, 2m);
        await AddPurchaseCostAsync(unused, 1m);
        _db.BOMLines.Add(new BOMLine { ParentPartId = product.Id, ChildPartId = bar.Id, Quantity = 3m });
        await _db.SaveChangesAsync();
        await AddOpenSalesOrderAsync(product, 200m);

        var results = await RunAsync();

        var barRow = results.Single(r => r.PartId == bar.Id);
        barRow.AnnualDemandQuantity.Should().Be(600m);
        barRow.AnnualUsageValue.Should().Be(1200m);
        results.Single(r => r.PartId == product.Id).AnnualUsageValue.Should().Be(10000m);
        var unusedRow = results.Single(r => r.PartId == unused.Id);
        unusedRow.AnnualUsageValue.Should().Be(0m);
        unusedRow.Classification.Should().Be(AbcClass.C);
        barRow.Rank.Should().BeLessThan(unusedRow.Rank);
    }

    [Fact]
    public async Task Apply_WritesEachPartsClass()
    {
        var product = await AddPartAsync("FG-3", standardCost: 10m);
        var spare = await AddPartAsync("SPARE");
        spare.AbcClass = AbcClass.A;
        await _db.SaveChangesAsync();
        await AddShipmentAsync(product, 100m, Now.AddDays(-5));
        var run = await new RunAbcClassificationHandler(_db, _clock.Object)
            .Handle(new RunAbcClassificationCommand(new AbcClassificationParametersModel()), CancellationToken.None);

        await new ApplyAbcClassificationHandler(_db).Handle(new ApplyAbcClassificationCommand(run.Id), CancellationToken.None);

        var classes = await _db.AbcClassifications.AsNoTracking().Where(c => c.RunId == run.Id).ToDictionaryAsync(c => c.PartId, c => c.Classification);
        var parts = await _db.Parts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.AbcClass);
        parts[product.Id].Should().Be(classes[product.Id]).And.Be(AbcClass.A);
        parts[spare.Id].Should().Be(AbcClass.C);
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "Part" && a.EntityId == spare.Id && a.Description.Contains("from A to C"));
    }

    [Fact]
    public async Task Apply_UnknownRun_IsNotFound()
    {
        var act = () => new ApplyAbcClassificationHandler(_db).Handle(new ApplyAbcClassificationCommand(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private async Task<List<AbcClassification>> RunAsync()
    {
        var run = await new RunAbcClassificationHandler(_db, _clock.Object)
            .Handle(new RunAbcClassificationCommand(new AbcClassificationParametersModel()), CancellationToken.None);
        return await _db.AbcClassifications.AsNoTracking().Where(c => c.RunId == run.Id).ToListAsync();
    }

    private async Task<Part> AddPartAsync(string partNumber, decimal? standardCost = null)
    {
        var part = new Part { PartNumber = partNumber, Name = partNumber, Status = PartStatus.Active, ManualCostOverride = standardCost };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task AddPurchaseCostAsync(Part part, decimal unitPrice)
    {
        var order = new PurchaseOrder { PONumber = $"PO-{part.PartNumber}", VendorId = 1 };
        order.Lines.Add(new PurchaseOrderLine { PartId = part.Id, Description = part.PartNumber, OrderedQuantity = 1m, UnitPrice = unitPrice });
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();
    }

    private async Task<SalesOrder> AddSalesOrderAsync(SalesOrderStatus status)
    {
        var order = new SalesOrder { OrderNumber = $"SO-{Guid.NewGuid():N}"[..12], CustomerId = 1, Status = status };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task AddOpenSalesOrderAsync(Part part, decimal quantity)
    {
        var order = await AddSalesOrderAsync(SalesOrderStatus.Confirmed);
        _db.SalesOrderLines.Add(new SalesOrderLine { SalesOrderId = order.Id, PartId = part.Id, Description = "x", Quantity = quantity, UnitPrice = 75m });
        await _db.SaveChangesAsync();
    }

    private async Task AddShipmentAsync(Part part, decimal quantity, DateTimeOffset shippedAt)
    {
        var order = await AddSalesOrderAsync(SalesOrderStatus.Shipped);
        var line = new SalesOrderLine { SalesOrderId = order.Id, PartId = part.Id, Description = "x", Quantity = quantity, ShippedQuantity = quantity, UnitPrice = 1m };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        var shipment = new Shipment { ShipmentNumber = $"SH-{Guid.NewGuid():N}"[..12], SalesOrderId = order.Id, Status = ShipmentStatus.Shipped, ShippedDate = shippedAt };
        shipment.Lines.Add(new ShipmentLine { SalesOrderLineId = line.Id, Quantity = quantity });
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
    }

    private async Task AddIssueAsync(Part part, decimal quantity, MaterialIssueType type, DateTimeOffset issuedAt)
    {
        _db.MaterialIssues.Add(new MaterialIssue { JobId = 1, PartId = part.Id, Quantity = quantity, UnitCost = 4m, IssuedById = 1, IssuedAt = issuedAt, IssueType = type });
        await _db.SaveChangesAsync();
    }
}
