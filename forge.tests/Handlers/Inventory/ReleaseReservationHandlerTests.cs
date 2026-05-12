using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Inventory;

public class ReleaseReservationHandlerTests
{
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly ReleaseReservationHandler _handler;

    private readonly Faker _faker = new();

    public ReleaseReservationHandlerTests()
    {
        _handler = new ReleaseReservationHandler(_inventoryRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidReservation_DecrementsReservedQuantity()
    {
        // Arrange
        var reservedQty = 5m;
        var initialReservedTotal = 12m;
        var reservationId = _faker.Random.Int(1, 100);

        var binContent = new BinContent
        {
            Id = _faker.Random.Int(1, 200),
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 20,
            ReservedQuantity = initialReservedTotal,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            PartId = 1,
            BinContentId = binContent.Id,
            Quantity = reservedQty,
            BinContent = binContent,
        };

        _inventoryRepo.Setup(r => r.FindReservationAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var command = new ReleaseReservationCommand(reservationId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        binContent.ReservedQuantity.Should().Be(initialReservedTotal - reservedQty);
        _inventoryRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidReservation_SetsDeletedAt()
    {
        // Arrange
        var reservationId = _faker.Random.Int(1, 100);

        var binContent = new BinContent
        {
            Id = 1,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 10,
            ReservedQuantity = 3,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            PartId = 1,
            BinContentId = binContent.Id,
            Quantity = 3,
            BinContent = binContent,
        };

        _inventoryRepo.Setup(r => r.FindReservationAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var command = new ReleaseReservationCommand(reservationId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        reservation.DeletedAt.Should().NotBeNull();
        reservation.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_ReservationNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var reservationId = 999;

        _inventoryRepo.Setup(r => r.FindReservationAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation?)null);

        var command = new ReleaseReservationCommand(reservationId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Reservation {reservationId}*not found*");
    }

    [Fact]
    public async Task Handle_ReleaseWouldGoNegative_ClampsReservedQuantityToZero()
    {
        // Arrange — reserved quantity on bin is less than reservation quantity (data inconsistency edge case)
        var reservationId = 5;

        var binContent = new BinContent
        {
            Id = 1,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 10,
            ReservedQuantity = 2, // less than the reservation quantity
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            PartId = 1,
            BinContentId = binContent.Id,
            Quantity = 5, // greater than ReservedQuantity
            BinContent = binContent,
        };

        _inventoryRepo.Setup(r => r.FindReservationAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var command = new ReleaseReservationCommand(reservationId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — Math.Max(0, ...) prevents negative reserved quantity
        binContent.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MultipleReservations_OnlyDecrementsReleasedAmount()
    {
        // Arrange
        var reservationId = 10;
        var otherReservationAmount = 8m;
        var thisReservationAmount = 3m;

        // Bin has 11 total reserved (8 from another reservation + 3 from this one)
        var binContent = new BinContent
        {
            Id = 2,
            LocationId = 1,
            EntityType = "part",
            EntityId = 1,
            Quantity = 30,
            ReservedQuantity = otherReservationAmount + thisReservationAmount,
            PlacedBy = 1,
            PlacedAt = DateTime.UtcNow,
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            PartId = 1,
            BinContentId = binContent.Id,
            Quantity = thisReservationAmount,
            BinContent = binContent,
        };

        _inventoryRepo.Setup(r => r.FindReservationAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var command = new ReleaseReservationCommand(reservationId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — only the released reservation quantity is subtracted
        binContent.ReservedQuantity.Should().Be(otherReservationAmount);
    }
}
