using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.Chat;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class SendChatRoomMessageHandlerTests
{
    private readonly SendChatRoomMessageHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Mock<IHubContext<ChatHub>> _chatHub = new();
    private readonly Faker _faker = new();

    public SendChatRoomMessageHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _chatHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _handler = new SendChatRoomMessageHandler(_db, _chatHub.Object);
    }

    [Fact]
    public async Task Handle_ValidMessage_CreatesMessageAndBroadcasts()
    {
        // Arrange — create user, room, and membership
        var user = new ApplicationUser
        {
            UserName = "test@test.com",
            Email = "test@test.com",
            FirstName = "Test",
            LastName = "User",
            Initials = "TU",
            AvatarColor = "#94a3b8",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var room = new ChatRoom { Name = "General", IsGroup = true };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();

        _db.Set<ChatRoomMember>().Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = user.Id });
        await _db.SaveChangesAsync();

        var command = new SendChatRoomMessageCommand(user.Id, room.Id, "Hello world!");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Content.Should().Be("Hello world!");
        result.SenderId.Should().Be(user.Id);

        _db.ChatMessages.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_NonMember_ThrowsUnauthorizedAccessException()
    {
        // Arrange — create user and room but no membership
        var user = new ApplicationUser
        {
            UserName = "test@test.com",
            Email = "test@test.com",
            FirstName = "Test",
            LastName = "User",
        };
        _db.Users.Add(user);

        var room = new ChatRoom { Name = "Private", IsGroup = true };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();

        var command = new SendChatRoomMessageCommand(user.Id, room.Id, "Test");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
