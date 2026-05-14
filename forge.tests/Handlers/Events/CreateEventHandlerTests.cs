using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Events;

public class CreateEventHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly CreateEventHandler _handler;

    private const int CreatorUserId = 1;

    public CreateEventHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        SetupHttpContext(CreatorUserId);
        _handler = new CreateEventHandler(_db, _httpContext.Object);
    }

    private void SetupHttpContext(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContext.Setup(h => h.HttpContext).Returns(httpContext);
    }

    private async Task<ApplicationUser> SeedUser(string first, string last)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first.ToLower()}@test.com",
            Email = $"{first.ToLower()}@test.com",
            FirstName = first,
            LastName = last,
            Initials = $"{first[0]}{last[0]}",
            AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesEventWithAttendees()
    {
        var creator = await SeedUser("Admin", "User");
        SetupHttpContext(creator.Id);

        var attendee1 = await SeedUser("John", "Doe");
        var attendee2 = await SeedUser("Jane", "Smith");

        var command = new CreateEventCommand(
            "Safety Meeting", "Quarterly safety review",
            DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(7).AddHours(1),
            "Conference Room A", "Meeting", true,
            new List<int> { attendee1.Id, attendee2.Id });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Title.Should().Be("Safety Meeting");
        result.EventType.Should().Be("Meeting");
        result.IsRequired.Should().BeTrue();
        result.Location.Should().Be("Conference Room A");
        result.Attendees.Should().HaveCount(2);
        result.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CreatesNotificationsForAttendees()
    {
        var creator = await SeedUser("Admin", "User");
        SetupHttpContext(creator.Id);

        var attendee = await SeedUser("Worker", "One");

        var command = new CreateEventCommand(
            "Training Session", null,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            null, "Training", false,
            new List<int> { attendee.Id });

        await _handler.Handle(command, CancellationToken.None);

        var notifications = _db.Notifications
            .Where(n => n.UserId == attendee.Id && n.Type == "event_invite")
            .ToList();

        notifications.Should().HaveCount(1);
        notifications[0].Title.Should().Contain("Training Session");
    }

    [Fact]
    public async Task Handle_DeduplicatesAttendeeUserIds()
    {
        var creator = await SeedUser("Admin", "User");
        SetupHttpContext(creator.Id);

        var attendee = await SeedUser("Dup", "User");

        var command = new CreateEventCommand(
            "Dup Test", null,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            null, "Other", false,
            new List<int> { attendee.Id, attendee.Id, attendee.Id });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Attendees.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_RequiredEvent_SetsWarningSeverityOnNotification()
    {
        var creator = await SeedUser("Admin", "User");
        SetupHttpContext(creator.Id);

        var attendee = await SeedUser("Worker", "Two");

        var command = new CreateEventCommand(
            "Mandatory Safety", null,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            null, "Safety", true,
            new List<int> { attendee.Id });

        await _handler.Handle(command, CancellationToken.None);

        var notification = _db.Notifications.First(n => n.UserId == attendee.Id);
        notification.Severity.Should().Be("warning");
    }
}
