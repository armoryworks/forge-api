using Bogus;
using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class GetClockStatusHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly Mock<IClockEventTypeService> _clockEventTypeService = new();
    private readonly GetClockStatusHandler _handler;
    private readonly Faker _faker = new();

    public GetClockStatusHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new GetClockStatusHandler(_db, _userManager.Object, _clockEventTypeService.Object);
    }

    [Fact]
    public async Task Handle_UserWithClockIn_ReturnsInStatus()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "john@example.com",
            Email = "john@example.com",
            Initials = "JD",
            AvatarColor = "#3b82f6",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Use a timestamp clearly today (noon UTC) to avoid date boundary issues with InMemory
        var todayNoon = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddHours(12);
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = user.Id,
            EventType = ClockEventType.ClockIn,
            EventTypeCode = "clock_in",
            Timestamp = todayNoon,
            Source = "kiosk",
        });
        await _db.SaveChangesAsync();

        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);
        _userManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "ProductionWorker" });

        _clockEventTypeService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("clock_in", "Clock In", "In", "clock_out", "work", true, false, "login", "#22c55e"),
                new("clock_out", "Clock Out", "Out", "clock_in", "work", false, false, "logout", "#ef4444"),
            });

        var query = new GetClockStatusQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var worker = result[0];
        worker.UserId.Should().Be(user.Id);
        worker.Status.Should().Be("In");
        worker.IsClockedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserWithNoEvents_ReturnsOutStatus()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Jane",
            LastName = "Smith",
            UserName = "jane@example.com",
            Email = "jane@example.com",
            Initials = "JS",
            AvatarColor = "#94a3b8",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);
        _userManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Engineer" });

        _clockEventTypeService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("clock_in", "Clock In", "In", "clock_out", "work", true, false, "login", "#22c55e"),
            });

        var query = new GetClockStatusQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var worker = result[0];
        worker.Status.Should().Be("Out");
        worker.IsClockedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserWithBreakEvent_ReturnsOnBreakStatus()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Bob",
            LastName = "Wilson",
            UserName = "bob@example.com",
            Email = "bob@example.com",
            Initials = "BW",
            AvatarColor = "#f59e0b",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Use timestamps clearly today to avoid date boundary issues with InMemory
        var todayNoon = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddHours(12);
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = user.Id,
            EventType = ClockEventType.ClockIn,
            EventTypeCode = "clock_in",
            Timestamp = todayNoon.AddHours(-2),
            Source = "kiosk",
        });
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = user.Id,
            EventType = ClockEventType.BreakStart,
            EventTypeCode = "break_start",
            Timestamp = todayNoon,
            Source = "kiosk",
        });
        await _db.SaveChangesAsync();

        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);
        _userManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "ProductionWorker" });

        _clockEventTypeService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("clock_in", "Clock In", "In", "clock_out", "work", true, false, "login", "#22c55e"),
                new("break_start", "Break Start", "On Break", "break_end", "break", true, false, "free_breakfast", "#f59e0b"),
                new("break_end", "Break End", "In", "break_start", "break", true, false, "free_breakfast", "#22c55e"),
            });

        var query = new GetClockStatusQuery();

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var worker = result[0];
        worker.Status.Should().Be("On Break");
        worker.IsClockedIn.Should().BeTrue(); // CountsAsActive = true
    }
}
