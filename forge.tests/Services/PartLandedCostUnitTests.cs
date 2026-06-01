using FluentAssertions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// UoM purchase-units effort — landed cost must be reported per base/stock unit even when the
/// PO line was priced per purchase unit ($50 per 4×8 sheet ÷ 32 sqft = $1.5625/sqft, not $50).
/// </summary>
public class PartLandedCostUnitTests
{
    [Fact]
    public async Task Landed_cost_is_per_base_unit_when_the_line_is_option_priced()
    {
        using var db = TestDbContextFactory.Create();

        var part = new Part { PartNumber = "TP-1", Name = "Thermoplastic" };
        var vendor = new Vendor { CompanyName = "Acme" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var option = new PartPurchaseUnit { PartId = part.Id, Label = "4x8 sheet", ContentQuantity = 32m };
        db.PartPurchaseUnits.Add(option);
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Received };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            PartId = part.Id,
            Description = "sheets",
            UnitPrice = 50m,            // per 4×8 sheet
            OrderedQuantity = 2m,       // in options
            PurchaseUnitId = option.Id,
        };
        db.PurchaseOrderLines.Add(line);
        await db.SaveChangesAsync();

        // AllocatedFreight set (non-null) so the receipt qualifies; 0 so cost == base price.
        db.ReceivingRecords.Add(new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id,
            QuantityReceived = 2m,      // in options
            AllocatedFreight = 0m,
            ReceiptNumber = "R-1",
        });
        await db.SaveChangesAsync();

        var tariff = new Mock<ITariffResolver>();
        tariff.Setup(t => t.ResolveAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        var currency = new Mock<ICurrencyService>();
        currency.Setup(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>())).ReturnsAsync("USD");

        var sut = new PartLandedCostService(db, tariff.Object, currency.Object);
        var result = await sut.GetForPartAsync(part.Id, 3, default);

        result.ReceiptCountUsed.Should().Be(1);
        result.AverageBaseUnitPrice.Should().Be(50m / 32m, "the $50 sheet price must divide by its 32 sqft content");
        result.AverageLandedUnitCost.Should().Be(50m / 32m, "freight + duty are 0, so landed == per-base-unit base price");
    }
}
