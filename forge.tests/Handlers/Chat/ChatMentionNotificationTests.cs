using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Chat;
using Forge.Api.Features.Notifications;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class ChatMentionNotificationTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISender> _mediator = new();
    private readonly List<CreateNotificationRequestModel> _notifications = [];

    public ChatMentionNotificationTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationResponseModel>, CancellationToken>((c, _) => _notifications.Add(((CreateNotificationCommand)c).Data));
    }

    private static IHubContext<ChatHub> Hub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private async Task<int> SeedRoomAsync()
    {
        _db.Users.AddRange(
            new ApplicationUser { Id = 1, UserName = "ada@t.com", FirstName = "Ada", LastName = "Lovelace" },
            new ApplicationUser { Id = 2, UserName = "ben@t.com", FirstName = "Ben", LastName = "Okafor" },
            new ApplicationUser { Id = 3, UserName = "cy@t.com", FirstName = "Cy", LastName = "Outsider" });
        var room = new ChatRoom { Id = 10, Name = "floor", IsGroup = true, CreatedById = 1 };
        _db.ChatRooms.Add(room);
        _db.ChatRoomMembers.AddRange(
            new ChatRoomMember { ChatRoomId = 10, UserId = 1 },
            new ChatRoomMember { ChatRoomId = 10, UserId = 2 });
        await _db.SaveChangesAsync();
        return room.Id;
    }

    [Fact]
    public async Task A_room_message_notifies_each_mentioned_member_once_and_nobody_else()
    {
        var roomId = await SeedRoomAsync();
        const string content = "@[user:2:Ben Okafor] @[user:2:Ben] @[user:3:Cy] @[user:1:Ada] see @[job:7:J-7]";

        await new SendChatRoomMessageHandler(_db, Hub(), _mediator.Object)
            .Handle(new SendChatRoomMessageCommand(1, roomId, content), CancellationToken.None);

        var notification = _notifications.Should().ContainSingle().Subject;
        notification.UserId.Should().Be(2);
        notification.Type.Should().Be("mention");
        notification.SenderId.Should().Be(1);
        notification.EntityType.Should().Be("ChatRoom");
        notification.EntityId.Should().Be(roomId);
        notification.Title.Should().Be("Lovelace, Ada mentioned you in chat");
        notification.Message.Should().Be("@Ben Okafor @Ben @Cy @Ada see @J-7");
    }

    [Fact]
    public async Task A_thread_reply_notifies_a_mentioned_member()
    {
        var roomId = await SeedRoomAsync();
        var parent = await new SendChatRoomMessageHandler(_db, Hub(), _mediator.Object)
            .Handle(new SendChatRoomMessageCommand(1, roomId, "Press 4 is down"), CancellationToken.None);

        await new ReplyInThreadHandler(_db, Hub(), _mediator.Object)
            .Handle(new ReplyInThreadCommand(2, parent.Id, "@[user:1:Ada] on it"), CancellationToken.None);

        _notifications.Should().ContainSingle(n => n.UserId == 1 && n.Message == "@Ada on it" && n.EntityId == roomId);
    }
}
