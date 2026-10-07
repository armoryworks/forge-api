using Bogus;
using FluentAssertions;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

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

        var definitions = new List<ClockEventTypeDefinition>
        {
            new("clock_in", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
            new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
        };
        var types = new Mock<IClockEventTypeService>();
        types.Setup(t => t.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => definitions.FirstOrDefault(d => d.Code == code));

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);

        _handler = new ClockInOutHandler(_db, types.Object, Mock.Of<IMediator>(), clock.Object);
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
    public async Task Handle_UnknownEventTypeCode_ThrowsKeyNotFoundException()
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
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*custom_event*");
        (await _db.ClockEvents.AnyAsync()).Should().BeFalse();
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
