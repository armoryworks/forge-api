using Bogus;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Shipments;

public class CreateShipmentHandlerTests
{
    private readonly Mock<IShipmentRepository> _shipmentRepo = new();
    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly CreateShipmentHandler _handler;

    private readonly Faker _faker = new();

    public CreateShipmentHandlerTests()
    {
        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1")]));
        var httpContext = new DefaultHttpContext { User = claims };
        _httpContext.Setup(x => x.HttpContext).Returns(httpContext);

        _handler = new CreateShipmentHandler(_shipmentRepo.Object, _orderRepo.Object, _mediator.Object, _httpContext.Object);
    }

    private SalesOrder CreateConfirmedOrder(int id, int lineId, int quantity, int shippedQuantity = 0)
    {
        return new SalesOrder
        {
            Id = id,
            OrderNumber = $"SO-{id:D4}",
            CustomerId = 1,
            Status = SalesOrderStatus.Confirmed,
            Customer = new Customer { Id = 1, Name = "Test Customer" },
            Lines =
            [
                new SalesOrderLine
                {
                    Id = lineId,
                    SalesOrderId = id,
                    Description = "Test Item",
                    Quantity = quantity,
                    ShippedQuantity = shippedQuantity,
                    UnitPrice = 10m,
                    LineNumber = 1,
                }
            ],
        };
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesShipmentAndReturnsListItem()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);
        var shipmentNumber = "SHP-0001";

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync(shipmentNumber);

        var command = new CreateShipmentCommand(
            1, null, "FedEx", "TRACK123", 15.00m, 5.5m, "Handle with care",
            [new CreateShipmentLineModel(10, 10, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ShipmentNumber.Should().Be(shipmentNumber);
        result.SalesOrderId.Should().Be(1);
        result.SalesOrderNumber.Should().Be("SO-0001");
        result.CustomerName.Should().Be("Test Customer");
        result.Carrier.Should().Be("FedEx");
        result.TrackingNumber.Should().Be("TRACK123");

        _shipmentRepo.Verify(r => r.AddAsync(It.Is<Shipment>(s =>
            s.ShipmentNumber == shipmentNumber &&
            s.Carrier == "FedEx" &&
            s.Lines.Count == 1
        ), It.IsAny<CancellationToken>()), Times.Once);

        _shipmentRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _orderRepo.Setup(r => r.FindWithDetailsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SalesOrder?)null);

        var command = new CreateShipmentCommand(
            999, null, null, null, null, null, null,
            [new CreateShipmentLineModel(1, 5, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_DraftOrder_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);
        order.Status = SalesOrderStatus.Draft;

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(10, 5, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Draft*Cancelled*");
    }

    [Fact]
    public async Task Handle_CancelledOrder_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);
        order.Status = SalesOrderStatus.Cancelled;

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(10, 5, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Draft*Cancelled*");
    }

    [Fact]
    public async Task Handle_OrderLineNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SHP-0001");

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(999, 5, null)]); // line 999 does not exist

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task Handle_ExceedsRemainingQuantity_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20, shippedQuantity: 15);

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SHP-0001");

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(10, 10, null)]); // only 5 remaining

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot ship 10*only 5 remaining*");
    }

    [Fact]
    public async Task Handle_FullyShipsAllLines_UpdatesOrderStatusToShipped()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SHP-0001");

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(10, 20, null)]); // ship all 20

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        order.Status.Should().Be(SalesOrderStatus.Shipped);
    }

    [Fact]
    public async Task Handle_PartialShipment_UpdatesOrderStatusToPartiallyShipped()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SHP-0001");

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null,
            [new CreateShipmentLineModel(10, 5, null)]); // ship only 5 of 20

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        order.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
        order.Lines.First().ShippedQuantity.Should().Be(5);
    }

    [Fact]
    public async Task Handle_UsesOrderShippingAddressWhenNotProvided()
    {
        // Arrange
        var order = CreateConfirmedOrder(1, 10, 20);
        order.ShippingAddressId = 42;

        _orderRepo.Setup(r => r.FindWithDetailsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _shipmentRepo.Setup(r => r.GenerateNextShipmentNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("SHP-0001");

        var command = new CreateShipmentCommand(
            1, null, null, null, null, null, null, // no shipping address
            [new CreateShipmentLineModel(10, 5, null)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _shipmentRepo.Verify(r => r.AddAsync(It.Is<Shipment>(s =>
            s.ShippingAddressId == 42
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
