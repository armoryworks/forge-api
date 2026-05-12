using System.Security.Claims;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public class AdjustStockHandlerTests
{
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly AdjustStockHandler _handler;

    private readonly Faker _faker = new();
    private readonly int _userId;

    public AdjustStockHandlerTests()
    {
        _userId = _faker.Random.Int(1, 100);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        _handler = new AdjustStockHandler(_inventoryRepo.Object, _httpContextAccessor.Object);
    }

    [Fact]
    public async Task Handle_ValidAdjustment_UpdatesQuantityAndCreatesMovement()
    {
        // Arrange
        var binContentId = _faker.Random.Int(1, 100);
        var locationId = _faker.Random.Int(1, 50);
        var currentQuantity = 10m;
        var newQuantity = 15;

        var binContent = new BinContent
        {
            Id = binContentId,
            LocationId = locationId,
            EntityType = "part",
            EntityId = _faker.Random.Int(1, 50),
            Quantity = currentQuantity,
            LotNumber = "LOT-001",
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new AdjustStockRequestModel(binContentId, newQuantity, "Recount correction", null);
        var command = new AdjustStockCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        binContent.Quantity.Should().Be(newQuantity);

        _inventoryRepo.Verify(r => r.AddMovementAsync(It.Is<BinMovement>(m =>
            m.Quantity == 5 &&
            m.Reason == BinMovementReason.Adjustment &&
            m.MovedBy == _userId &&
            m.ToLocationId == locationId &&
            m.FromLocationId == null
        ), It.IsAny<CancellationToken>()), Times.Once);

        _inventoryRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DecreaseQuantity_SetsFromLocation()
    {
        // Arrange
        var binContentId = 5;
        var locationId = 10;

        var binContent = new BinContent
        {
            Id = binContentId,
            LocationId = locationId,
            EntityType = "part",
            EntityId = 1,
            Quantity = 20,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new AdjustStockRequestModel(binContentId, 12, "Damaged items removed", null);
        var command = new AdjustStockCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        binContent.Quantity.Should().Be(12);

        _inventoryRepo.Verify(r => r.AddMovementAsync(It.Is<BinMovement>(m =>
            m.Quantity == 8 &&
            m.FromLocationId == locationId &&
            m.ToLocationId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BinContentNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var binContentId = 999;

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);

        var data = new AdjustStockRequestModel(binContentId, 10, "Test", null);
        var command = new AdjustStockCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{binContentId}*not found*");
    }

    [Fact]
    public async Task Handle_AdjustToZero_SetsRemovedAtAndRemovedBy()
    {
        // Arrange
        var binContentId = 3;

        var binContent = new BinContent
        {
            Id = binContentId,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 5,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new AdjustStockRequestModel(binContentId, 0, "Bin emptied", null);
        var command = new AdjustStockCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        binContent.Quantity.Should().Be(0);
        binContent.RemovedAt.Should().NotBeNull();
        binContent.RemovedBy.Should().Be(_userId);
    }

    [Fact]
    public async Task Handle_AdjustToNonZero_DoesNotSetRemovedAt()
    {
        // Arrange
        var binContentId = 4;

        var binContent = new BinContent
        {
            Id = binContentId,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 10,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new AdjustStockRequestModel(binContentId, 7, "Adjustment", null);
        var command = new AdjustStockCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        binContent.RemovedAt.Should().BeNull();
        binContent.RemovedBy.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PreservesLotNumberOnMovement()
    {
        // Arrange
        var lotNumber = "LOT-2025-042";
        var binContent = new BinContent
        {
            Id = 1,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 10,
            LotNumber = lotNumber,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new AdjustStockRequestModel(1, 15, "Received more", null);
        var command = new AdjustStockCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _inventoryRepo.Verify(r => r.AddMovementAsync(It.Is<BinMovement>(m =>
            m.LotNumber == lotNumber
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
