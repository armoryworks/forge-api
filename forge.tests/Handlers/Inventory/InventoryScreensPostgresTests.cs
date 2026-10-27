using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Lots;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

[Collection(PostgresCollection.Name)]
public sealed class InventoryScreensPostgresTests(PostgresFixture fixture)
{
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Reserving_for_a_sales_order_line_holds_the_bin_and_stops_at_the_open_quantity()
    {
        int partId, binContentId, lineId, salesOrderId;
        string orderNumber;
        await using (var seed = fixture.CreateContext())
        {
            var part = new Part { PartNumber = Unique("RSV"), Description = "Reserved part" };
            var customer = new Customer { Name = "Reserving customer" };
            var bin = new StorageLocation { Name = Unique("RB"), LocationType = LocationType.Bin, IsActive = true };
            seed.AddRange(part, customer, bin);
            await seed.SaveChangesAsync();
            var so = new SalesOrder { OrderNumber = Unique("SO"), CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
            seed.SalesOrders.Add(so);
            await seed.SaveChangesAsync();
            var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Description = "Line", Quantity = 25m, UnitPrice = 3m, LineNumber = 1 };
            var content = new BinContent
            {
                LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 100m,
                Status = BinContentStatus.Stored, PlacedAt = DateTimeOffset.UtcNow,
            };
            seed.SalesOrderLines.Add(line);
            seed.BinContents.Add(content);
            await seed.SaveChangesAsync();
            (partId, binContentId, lineId, salesOrderId, orderNumber) = (part.Id, content.Id, line.Id, so.Id, so.OrderNumber);
        }

        await using (var db = fixture.CreateContext())
        {
            var handler = new CreateReservationHandler(new InventoryRepository(db), db);
            await handler.Handle(new CreateReservationCommand(
                new CreateReservationRequestModel(partId, binContentId, null, lineId, 20m, null)), CancellationToken.None);

            var act = () => handler.Handle(new CreateReservationCommand(
                new CreateReservationRequestModel(partId, binContentId, null, lineId, 6m, null)), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only 5 is still open*");
        }

        await using var check = fixture.CreateContext();
        (await check.BinContents.SingleAsync(b => b.Id == binContentId)).ReservedQuantity.Should().Be(20m);
        var forOrder = await new InventoryRepository(check).GetReservationsAsync(null, null, salesOrderId, null, CancellationToken.None);
        forOrder.Should().ContainSingle().Which.SalesOrderNumber.Should().Be(orderNumber);
        var reservable = await new GetReservableSalesOrderLinesHandler(check)
            .Handle(new GetReservableSalesOrderLinesQuery(partId, salesOrderId), CancellationToken.None);
        reservable.Should().ContainSingle().Which.ReservedQuantity.Should().Be(20m);
    }

    [Fact]
    public async Task A_receipt_stocks_the_lot_and_stamps_its_expiry_from_the_shelf_life()
    {
        int poId, lineId, binId;
        await using (var seed = fixture.CreateContext())
        {
            var vendor = new Vendor { CompanyName = "Shelf life supply" };
            var bin = new StorageLocation { Name = Unique("EB"), LocationType = LocationType.Bin, IsActive = true };
            var part = new Part { PartNumber = Unique("EXP"), Description = "Adhesive", TraceabilityType = TraceabilityType.Lot, ShelfLifeDays = 90 };
            seed.AddRange(vendor, bin, part);
            await seed.SaveChangesAsync();
            var po = new PurchaseOrder { PONumber = Unique("PO"), VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
            var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 50m, UnitPrice = 4m };
            seed.PurchaseOrderLines.Add(line);
            await seed.SaveChangesAsync();
            (poId, lineId, binId) = (po.Id, line.Id, bin.Id);
        }

        var lotNumber = Unique("LOT");
        var before = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateContext())
        {
            var http = new Mock<IHttpContextAccessor>();
            http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "Test")),
            });
            await new ReceiveItemsHandler(
                    new PurchaseOrderRepository(db), new SystemClock(), Mock.Of<MediatR.IMediator>(), http.Object,
                    db, null, new InventoryRepository(db), new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability),
                    Mock.Of<IBarcodeService>())
                .Handle(new ReceiveItemsCommand(poId, [new ReceiveLineModel(lineId, 12m, binId, null, LotNumber: lotNumber)]), CancellationToken.None);
        }

        await using var check = fixture.CreateContext();
        var lot = await check.LotRecords.SingleAsync(l => l.LotNumber == lotNumber);
        lot.Quantity.Should().Be(12m);
        lot.ExpirationDate.Should().NotBeNull();
        lot.ExpirationDate!.Value.UtcDateTime.Should().BeOneOf(before.UtcDateTime.Date.AddDays(90), DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(90));
        (await check.BinContents.Where(b => b.LocationId == binId && b.LotNumber == lotNumber).SumAsync(b => b.Quantity)).Should().Be(12m);
    }
}
