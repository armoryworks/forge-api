using FluentAssertions;

using Forge.Api.Features.Events;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Events;

public class DeleteEventHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly DeleteEventHandler _handler;

    public DeleteEventHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new DeleteEventHandler(_db);
    }

    [Fact]
    public async Task Handle_SoftCancelsEvent()
    {
        var user = new ApplicationUser
        {
            UserName = "admin@test.com", Email = "admin@test.com",
            FirstName = "Admin", LastName = "User", Initials = "AU", AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var evt = new Event
        {
            Title = "To Cancel",
            EventType = EventType.Meeting,
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedByUserId = user.Id,
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        await _handler.Handle(new DeleteEventCommand(evt.Id), CancellationToken.None);

        var updated = _db.Events.First(e => e.Id == evt.Id);
        updated.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonExistentEvent_ThrowsKeyNotFoundException()
    {
        var act = () => _handler.Handle(new DeleteEventCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
