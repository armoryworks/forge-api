using FluentAssertions;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Tests.Handlers.Shipments;

public class SalesOrderShippingTests
{
    private static SalesOrder Order(SalesOrderStatus status, decimal quantity, decimal shippedQuantity)
        => new()
        {
            Id = 1,
            OrderNumber = "SO-0001",
            Status = status,
            Lines = [new SalesOrderLine { Id = 10, SalesOrderId = 1, Quantity = quantity, ShippedQuantity = shippedQuantity, LineNumber = 1 }],
        };

    private static Shipment AddShipment(SalesOrder order, ShipmentStatus status, decimal quantity, int lineId = 10)
    {
        var shipment = new Shipment { SalesOrderId = order.Id, ShipmentNumber = $"SH-{order.Shipments.Count + 1}", Status = status };
        shipment.Lines.Add(new ShipmentLine { SalesOrderLineId = lineId, Quantity = quantity });
        order.Shipments.Add(shipment);
        return shipment;
    }

    [Fact]
    public void OpenToShip_NetsOutLiveShipments_ButNotCancelledOnes()
    {
        var order = Order(SalesOrderStatus.Confirmed, 20m, 0m);
        AddShipment(order, ShipmentStatus.Pending, 6m);
        AddShipment(order, ShipmentStatus.Delivered, 4m);
        AddShipment(order, ShipmentStatus.Cancelled, 9m);

        SalesOrderShipping.OpenToShip(order, order.Lines.First()).Should().Be(10m);
    }

    [Fact]
    public void OpenToShip_ShippedQuantityBeyondItsShipments_StillCountsAsTaken()
    {
        var order = Order(SalesOrderStatus.PartiallyShipped, 20m, 12m);
        AddShipment(order, ShipmentStatus.Pending, 3m);

        SalesOrderShipping.OpenToShip(order, order.Lines.First()).Should().Be(8m);
    }

    [Fact]
    public void RecordShipped_CountsOnlyShippedShipments_AndMarksTheOrderPartiallyShipped()
    {
        var order = Order(SalesOrderStatus.Confirmed, 20m, 0m);
        AddShipment(order, ShipmentStatus.Pending, 5m);
        AddShipment(order, ShipmentStatus.Shipped, 7m);
        AddShipment(order, ShipmentStatus.InTransit, 1m);

        SalesOrderShipping.RecordShipped(order);

        order.Lines.First().ShippedQuantity.Should().Be(8m);
        order.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public void RecordShipped_LineInflatedByAPendingShipment_DropsBackToWhatShipped()
    {
        var order = Order(SalesOrderStatus.Shipped, 20m, 20m);
        AddShipment(order, ShipmentStatus.Pending, 8m);
        AddShipment(order, ShipmentStatus.Shipped, 12m);

        SalesOrderShipping.RecordShipped(order);

        order.Lines.First().ShippedQuantity.Should().Be(12m);
        order.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
    }

    [Fact]
    public void RecordShipped_EveryShipmentShipped_MarksTheOrderShipped()
    {
        var order = Order(SalesOrderStatus.PartiallyShipped, 20m, 5m);
        AddShipment(order, ShipmentStatus.Delivered, 5m);
        AddShipment(order, ShipmentStatus.Shipped, 15m);

        SalesOrderShipping.RecordShipped(order);

        order.Lines.First().ShippedQuantity.Should().Be(20m);
        order.Status.Should().Be(SalesOrderStatus.Shipped);
    }

    [Fact]
    public void RecordShipped_EveryGoodsLineShipped_MarksTheOrderShippedWhileItsServiceLineAwaitsInvoicing()
    {
        var order = Order(SalesOrderStatus.Confirmed, 20m, 0m);
        order.Lines.Add(new SalesOrderLine
        {
            Id = 11, SalesOrderId = 1, PartId = 77, Description = "Tooling NRE", Quantity = 1, UnitPrice = 6800m, LineNumber = 2,
            Part = new Part { Id = 77, PartNumber = "NRE-T112", Name = "Tooling NRE", InventoryClass = InventoryClass.Service },
        });
        AddShipment(order, ShipmentStatus.Shipped, 20m);

        SalesOrderShipping.RecordShipped(order);

        order.Status.Should().Be(SalesOrderStatus.Shipped);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Completed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void RecordShipped_ClosedOrder_KeepsItsStatus(SalesOrderStatus status)
    {
        var order = Order(status, 20m, 0m);
        AddShipment(order, ShipmentStatus.Shipped, 20m);

        SalesOrderShipping.RecordShipped(order);

        order.Lines.First().ShippedQuantity.Should().Be(20m);
        order.Status.Should().Be(status);
    }
}
