using FluentAssertions;
using Moq;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Shipments;

public class GetShipmentCarrierNameTests
{
    [Fact]
    public async Task Detail_falls_back_to_the_assigned_carrier_name()
    {
        var shipment = new Shipment
        {
            Id = 1,
            ShipmentNumber = "SHP-0001",
            SalesOrderId = 1,
            SalesOrder = new SalesOrder { Id = 1, OrderNumber = "SO-0001", CustomerId = 1, Customer = new Customer { Id = 1, Name = "Acme" } },
            CarrierId = 5,
            AssignedCarrier = new Carrier { Id = 5, Name = "Old Dominion" },
        };
        var repo = new Mock<IShipmentRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        var result = await new GetShipmentByIdHandler(repo.Object)
            .Handle(new GetShipmentByIdQuery(1), CancellationToken.None);

        result.Carrier.Should().Be("Old Dominion");
        result.CarrierId.Should().Be(5);
    }

    [Fact]
    public async Task Detail_lines_carry_the_part_number()
    {
        var shipment = new Shipment
        {
            Id = 2,
            ShipmentNumber = "SHP-0002",
            SalesOrderId = 1,
            SalesOrder = new SalesOrder { Id = 1, OrderNumber = "SO-0001", CustomerId = 1, Customer = new Customer { Id = 1, Name = "Acme" } },
            Lines = [new ShipmentLine { Id = 9, PartId = 3, Quantity = 24000m, Part = new Part { Id = 3, PartNumber = "RS-PCABS-BLK", Name = "Resin" } }],
        };
        var repo = new Mock<IShipmentRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        var result = await new GetShipmentByIdHandler(repo.Object)
            .Handle(new GetShipmentByIdQuery(2), CancellationToken.None);

        result.Lines.Single().PartNumber.Should().Be("RS-PCABS-BLK");
    }
}
