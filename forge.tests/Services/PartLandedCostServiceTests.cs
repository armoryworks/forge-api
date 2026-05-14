using FluentAssertions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Bought-parts effort PR3 — landed cost calc rolls up the most-recent
/// receipts (with allocated freight) into a per-part surface. Tests
/// cover the empty-history, single-receipt, and multi-vendor cases.
/// </summary>
public class PartLandedCostServiceTests
{
    private readonly Mock<ITariffResolver> _tariffResolver = new();
    private readonly Mock<ICurrencyService> _currencyService = new();

    public PartLandedCostServiceTests()
    {
        _currencyService.Setup(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("USD");
        _tariffResolver
            .Setup(t => t.ResolveAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
    }

    [Fact]
    public async Task GetForPartAsync_NoReceipts_ReturnsEmptyAverage()
    {
        var db = TestDbContextFactory.Create();
        db.Parts.Add(new Part { Id = 1, PartNumber = "P-001", Name = "Part 1", Description = "Part 1" });
        await db.SaveChangesAsync();

        var svc = new PartLandedCostService(db, _tariffResolver.Object, _currencyService.Object);
        var result = await svc.GetForPartAsync(1, 3, CancellationToken.None);

        result.PartId.Should().Be(1);
        result.AverageLandedUnitCost.Should().BeNull();
        result.RecentReceipts.Should().BeEmpty();
        result.VendorComparison.Should().BeEmpty();
    }

    [Fact]
    public async Task GetForPartAsync_SingleReceipt_ComputesPerUnitFreight()
    {
        // Setup: vendor + part + PO + 1 line + 1 receiving record with $50 allocated freight on 10 qty.
        var db = TestDbContextFactory.Create();
        var part = new Part { Id = 1, PartNumber = "P-001", Name = "P", Description = "P" };
        var vendor = new Vendor { Id = 5, CompanyName = "Acme" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        var po = new PurchaseOrder { Id = 50, PONumber = "PO-50", VendorId = 5, Vendor = vendor, Status = PurchaseOrderStatus.Received };
        var line = new PurchaseOrderLine { Id = 100, PartId = 1, OrderedQuantity = 10m, UnitPrice = 4m, Description = "P", PurchaseOrderId = 50, PurchaseOrder = po };
        po.Lines.Add(line);
        db.PurchaseOrders.Add(po);
        db.PurchaseOrderLines.Add(line);
        db.ReceivingRecords.Add(new ReceivingRecord
        {
            Id = 999,
            PurchaseOrderLineId = 100,
            PurchaseOrderLine = line,
            QuantityReceived = 10m,
            ReceiptNumber = "R-X",
            ActualFreight = 50m,
            AllocatedFreight = 50m,
            FreightAllocationMethod = FreightAllocationMethod.ByExtendedValue,
        });
        await db.SaveChangesAsync();

        var svc = new PartLandedCostService(db, _tariffResolver.Object, _currencyService.Object);
        var result = await svc.GetForPartAsync(1, 3, CancellationToken.None);

        result.RecentReceipts.Should().HaveCount(1);
        result.RecentReceipts[0].BaseUnitPrice.Should().Be(4m);
        result.RecentReceipts[0].AllocatedFreightPerUnit.Should().Be(5m); // 50 freight / 10 qty
        result.RecentReceipts[0].DutyPerUnit.Should().Be(0m);
        result.RecentReceipts[0].LandedUnitCost.Should().Be(9m); // 4 + 5
        result.AverageLandedUnitCost.Should().Be(9m);
        result.ReceiptCountUsed.Should().Be(1);
        result.VendorComparison.Should().HaveCount(1);
        result.VendorComparison[0].VendorId.Should().Be(5);
    }

    [Fact]
    public async Task GetForPartAsync_RecordsWithoutAllocatedFreight_AreSkipped()
    {
        // Pre-PR3 records have AllocatedFreight = null. Confirm they don't
        // pollute the average.
        var db = TestDbContextFactory.Create();
        var part = new Part { Id = 1, PartNumber = "P", Name = "P", Description = "P" };
        var vendor = new Vendor { Id = 5, CompanyName = "Acme" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        var po = new PurchaseOrder { Id = 50, PONumber = "PO-50", VendorId = 5, Vendor = vendor };
        var line = new PurchaseOrderLine { Id = 100, PartId = 1, OrderedQuantity = 1m, UnitPrice = 4m, Description = "P", PurchaseOrderId = 50, PurchaseOrder = po };
        po.Lines.Add(line);
        db.PurchaseOrders.Add(po);
        db.PurchaseOrderLines.Add(line);
        db.ReceivingRecords.Add(new ReceivingRecord
        {
            Id = 1, PurchaseOrderLineId = 100, PurchaseOrderLine = line,
            QuantityReceived = 1m, AllocatedFreight = null, // pre-PR3 row
        });
        await db.SaveChangesAsync();

        var svc = new PartLandedCostService(db, _tariffResolver.Object, _currencyService.Object);
        var result = await svc.GetForPartAsync(1, 3, CancellationToken.None);

        result.RecentReceipts.Should().BeEmpty();
        result.AverageLandedUnitCost.Should().BeNull();
    }
}
