using Bogus;
using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class CreateClockInOutHandlerTests
{
    private readonly ClockInOutHandler _handler;
    private readonly AppDbContext _db;
    private readonly Faker _faker = new();

    public CreateClockInOutHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new ClockInOutHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidEventTypeCode_CreatesClockEvent()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            UserName = _faker.Internet.Email(),
            Email = _faker.Internet.Email(),
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var command = new ClockInOutCommand(user.Id, "clock_in");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var clockEvent = await _db.ClockEvents.FirstOrDefaultAsync();
        clockEvent.Should().NotBeNull();
        clockEvent!.UserId.Should().Be(user.Id);
        clockEvent.EventTypeCode.Should().Be("clock_in");
        clockEvent.Source.Should().Be("kiosk");
    }

    [Fact]
    public async Task Handle_NonExistentUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        var command = new ClockInOutCommand(99999, "clock_in");

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99999*");
    }

    [Fact]
    public async Task Handle_KnownEnumEventType_ParsesLegacyEnum()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            UserName = _faker.Internet.Email(),
            Email = _faker.Internet.Email(),
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var command = new ClockInOutCommand(user.Id, "ClockIn");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var clockEvent = await _db.ClockEvents.FirstOrDefaultAsync();
        clockEvent.Should().NotBeNull();
        clockEvent!.EventType.Should().Be(ClockEventType.ClockIn);
        clockEvent.EventTypeCode.Should().Be("ClockIn");
    }

    [Fact]
    public async Task Handle_UnknownEnumEventType_DefaultsToClockIn()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            UserName = _faker.Internet.Email(),
            Email = _faker.Internet.Email(),
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var command = new ClockInOutCommand(user.Id, "custom_event");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var clockEvent = await _db.ClockEvents.FirstOrDefaultAsync();
        clockEvent.Should().NotBeNull();
        clockEvent!.EventType.Should().Be(ClockEventType.ClockIn);
        clockEvent.EventTypeCode.Should().Be("custom_event");
    }

    [Fact]
    public async Task Handle_ValidEvent_SetsTimestamp()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Test",
            LastName = "User",
            UserName = "test@example.com",
            Email = "test@example.com",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        var command = new ClockInOutCommand(user.Id, "clock_in");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var clockEvent = await _db.ClockEvents.FirstOrDefaultAsync();
        clockEvent.Should().NotBeNull();
        clockEvent!.Timestamp.Should().BeOnOrAfter(before);
        clockEvent.Timestamp.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
