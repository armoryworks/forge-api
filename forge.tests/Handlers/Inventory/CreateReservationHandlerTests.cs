using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class CreateReservationHandlerTests
{
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly AppDbContext _dbContext;
    private readonly CreateReservationHandler _handler;

    private readonly Faker _faker = new();

    public CreateReservationHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _handler = new CreateReservationHandler(_inventoryRepo.Object, _dbContext);
    }

    [Fact]
    public async Task Handle_ValidReservation_CreatesReservationAndIncrementsReservedQuantity()
    {
        // Arrange
        var part = new Part
        {
            PartNumber = $"PN-{_faker.Random.AlphaNumeric(6)}",
            Description = _faker.Commerce.ProductName(),
        };
        _dbContext.Parts.Add(part);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Id = 1, Name = "Shelf A" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        var binContent = new BinContent
        {
            LocationId = location.Id,
            Location = location, // populate navigation property for BuildPath
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 20,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new CreateReservationRequestModel(part.Id, 1, null, null, 5, "For order");
        var command = new CreateReservationCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.PartId.Should().Be(part.Id);
        result.Quantity.Should().Be(5);

        binContent.ReservedQuantity.Should().Be(5);

        _inventoryRepo.Verify(r => r.AddReservationAsync(It.Is<Reservation>(res =>
            res.PartId == part.Id &&
            res.Quantity == 5 &&
            res.Notes == "For order"
        ), It.IsAny<CancellationToken>()), Times.Once);

        _inventoryRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InsufficientStock_ThrowsInvalidOperationException()
    {
        // Arrange — insufficient stock check happens before part/location lookup, no need for Location nav
        var part = new Part { PartNumber = "PN-001", Description = "Widget" };
        _dbContext.Parts.Add(part);
        await _dbContext.SaveChangesAsync();

        var binContent = new BinContent
        {
            LocationId = 1,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 10,
            ReservedQuantity = 7,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        // Only 3 available (10 - 7 reserved), but requesting 5
        var data = new CreateReservationRequestModel(part.Id, 1, null, null, 5, null);
        var command = new CreateReservationCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot reserve 5*only 3 available*");
    }

    [Fact]
    public async Task Handle_BinContentNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var binContentId = 999;

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(binContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);

        var data = new CreateReservationRequestModel(1, binContentId, null, null, 1, null);
        var command = new CreateReservationCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{binContentId}*not found*");
    }

    [Fact]
    public async Task Handle_PartNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var partId = 888;

        var location = new StorageLocation { Id = 1, Name = "Bin A" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        var binContent = new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = 1,
            Quantity = 10,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        // partId 888 is not in DB — the check occurs after the stock availability check
        var data = new CreateReservationRequestModel(partId, 1, null, null, 2, null);
        var command = new CreateReservationCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Part {partId}*not found*");
    }

    [Fact]
    public async Task Handle_ExactAvailableQuantity_Succeeds()
    {
        // Arrange
        var part = new Part { PartNumber = "PN-002", Description = "Bolt" };
        _dbContext.Parts.Add(part);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Id = 1, Name = "Bin B" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        var available = 4m;
        var binContent = new BinContent
        {
            LocationId = location.Id,
            Location = location,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 10,
            ReservedQuantity = 6, // available = 4
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new CreateReservationRequestModel(part.Id, 1, null, null, available, null);
        var command = new CreateReservationCommand(data);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert — should not throw (requesting exactly what's available)
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_WithJobId_IncludesJobInfoInResponse()
    {
        // Arrange
        var part = new Part { PartNumber = "PN-003", Description = "Gear" };
        _dbContext.Parts.Add(part);

        var job = new Job
        {
            JobNumber = "JOB-0099",
            Title = "Assembly Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
        };
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var location = new StorageLocation { Id = 1, Name = "Rack C" };
        _dbContext.StorageLocations.Add(location);
        await _dbContext.SaveChangesAsync();

        var binContent = new BinContent
        {
            LocationId = location.Id,
            Location = location,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 50,
            ReservedQuantity = 0,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        _inventoryRepo.Setup(r => r.FindBinContentWithLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binContent);

        var data = new CreateReservationRequestModel(part.Id, 1, job.Id, null, 3, null);
        var command = new CreateReservationCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.JobId.Should().Be(job.Id);
        result.JobTitle.Should().Be("Assembly Job");
        result.JobNumber.Should().Be("JOB-0099");
    }
}
