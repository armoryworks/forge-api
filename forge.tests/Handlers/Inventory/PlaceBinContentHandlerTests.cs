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

public class PlaceBinContentHandlerTests
{
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly PlaceBinContentHandler _handler;

    private readonly Faker _faker = new();
    private readonly int _userId;

    public PlaceBinContentHandlerTests()
    {
        _userId = _faker.Random.Int(1, 50);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        _handler = new PlaceBinContentHandler(_inventoryRepo.Object, _httpContextAccessor.Object);
    }

    [Fact]
    public async Task Handle_ValidBinLocation_PlacesContentAndReturnsResult()
    {
        // Arrange
        var locationId = _faker.Random.Int(1, 100);
        var location = new StorageLocation
        {
            Id = locationId,
            Name = $"BIN-{_faker.Random.AlphaNumeric(4).ToUpper()}",
            LocationType = LocationType.Bin,
        };

        _inventoryRepo.Setup(r => r.FindLocationAsync(locationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var data = new PlaceBinContentRequestModel(
            locationId, "Part", 42, 10m, "LOT-001", null, BinContentStatus.Stored, "Test placement");

        var command = new PlaceBinContentCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.LocationId.Should().Be(locationId);
        result.EntityType.Should().Be("Part");
        result.EntityId.Should().Be(42);
        result.Quantity.Should().Be(10m);

        _inventoryRepo.Verify(r => r.AddBinContentAsync(It.Is<BinContent>(bc =>
            bc.LocationId == locationId &&
            bc.EntityType == "Part" &&
            bc.EntityId == 42 &&
            bc.Quantity == 10m &&
            bc.LotNumber == "LOT-001" &&
            bc.PlacedBy == _userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CreatesBinMovementAuditRecord()
    {
        // Arrange
        var locationId = _faker.Random.Int(1, 100);
        var location = new StorageLocation
        {
            Id = locationId,
            Name = "BIN-A1",
            LocationType = LocationType.Bin,
        };

        _inventoryRepo.Setup(r => r.FindLocationAsync(locationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var data = new PlaceBinContentRequestModel(
            locationId, "Part", 5, 25m, null, null, BinContentStatus.Stored, null);

        var command = new PlaceBinContentCommand(data);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _inventoryRepo.Verify(r => r.AddMovementAsync(It.Is<BinMovement>(m =>
            m.EntityType == "Part" &&
            m.EntityId == 5 &&
            m.Quantity == 25m &&
            m.ToLocationId == locationId &&
            m.MovedBy == _userId &&
            m.Reason == BinMovementReason.Receive
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LocationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var locationId = _faker.Random.Int(1, 100);

        _inventoryRepo.Setup(r => r.FindLocationAsync(locationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StorageLocation?)null);

        var data = new PlaceBinContentRequestModel(
            locationId, "Part", 1, 5m, null, null, BinContentStatus.Stored, null);

        var command = new PlaceBinContentCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Location*");
    }

    [Fact]
    public async Task Handle_NonBinLocation_ThrowsInvalidOperationException()
    {
        // Arrange
        var locationId = _faker.Random.Int(1, 100);
        var location = new StorageLocation
        {
            Id = locationId,
            Name = "ZONE-A",
            LocationType = LocationType.Area,
        };

        _inventoryRepo.Setup(r => r.FindLocationAsync(locationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var data = new PlaceBinContentRequestModel(
            locationId, "Part", 1, 5m, null, null, BinContentStatus.Stored, null);

        var command = new PlaceBinContentCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bin-level*");
    }
}
