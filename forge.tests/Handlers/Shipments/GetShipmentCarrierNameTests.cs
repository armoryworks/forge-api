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
    public async Task Detail_names_the_linked_invoice_by_its_number()
    {
        var shipment = new Shipment
        {
            Id = 1,
            ShipmentNumber = "SHP-0001",
            SalesOrderId = 1,
            SalesOrder = new SalesOrder { Id = 1, OrderNumber = "SO-0001", CustomerId = 1, Customer = new Customer { Id = 1, Name = "Acme" } },
            Invoice = new Invoice { Id = 42, InvoiceNumber = "INV-00042" },
        };
        var repo = new Mock<IShipmentRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        var result = await new GetShipmentByIdHandler(repo.Object)
            .Handle(new GetShipmentByIdQuery(1), CancellationToken.None);

        result.InvoiceId.Should().Be(42);
        result.InvoiceNumber.Should().Be("INV-00042");
    }
}
