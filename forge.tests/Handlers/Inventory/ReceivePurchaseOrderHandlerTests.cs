using System.Security.Claims;
using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public class ReceivePurchaseOrderHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly ReceivePurchaseOrderHandler _handler;
    private readonly Faker _faker = new();
    private readonly int _userId;

    public ReceivePurchaseOrderHandlerTests()
    {
        _userId = _faker.Random.Int(1, 50);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId.ToString()),
            new(ClaimTypes.Name, "Test User"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        _handler = new ReceivePurchaseOrderHandler(_poRepo.Object, _inventoryRepo.Object, _httpContextAccessor.Object);
    }

    [Fact]
    public async Task Handle_ValidReceive_UpdatesLineAndReturnsRecord()
    {
        // Arrange
        var lineId = _faker.Random.Int(1, 100);
        var line = new PurchaseOrderLine
        {
            Id = lineId,
            PartId = 5,
            OrderedQuantity = 100,
            ReceivedQuantity = 20,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-001" },
        };

        _poRepo.Setup(r => r.FindLineAsync(lineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        var data = new ReceivePurchaseOrderRequestModel(lineId, 30, null, null, "Test receive");
        var command = new ReceivePurchaseOrderCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.PurchaseOrderLineId.Should().Be(lineId);
        result.QuantityReceived.Should().Be(30);
        line.ReceivedQuantity.Should().Be(50); // 20 + 30
    }

    [Fact]
    public async Task Handle_ExceedsRemainingQuantity_ThrowsInvalidOperationException()
    {
        var lineId = _faker.Random.Int(1, 100);
        var line = new PurchaseOrderLine
        {
            Id = lineId,
            PartId = 5,
            OrderedQuantity = 100,
            ReceivedQuantity = 95,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-002" },
        };

        _poRepo.Setup(r => r.FindLineAsync(lineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        var data = new ReceivePurchaseOrderRequestModel(lineId, 10, null, null, null);
        var command = new ReceivePurchaseOrderCommand(data);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*only*remaining*");
    }

    [Fact]
    public async Task Handle_LineNotFound_ThrowsKeyNotFoundException()
    {
        _poRepo.Setup(r => r.FindLineAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderLine?)null);

        var data = new ReceivePurchaseOrderRequestModel(99999, 1, null, null, null);
        var command = new ReceivePurchaseOrderCommand(data);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
