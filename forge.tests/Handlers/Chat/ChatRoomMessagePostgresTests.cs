using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Forge.Api.Features.Chat;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

[Collection(PostgresCollection.Name)]
public sealed class ChatRoomMessagePostgresTests(PostgresFixture fixture)
{
    private static IHubContext<ChatHub> Hub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private static async Task<ApplicationUser> AddUserAsync(AppDbContext db, string first)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            UserName = $"{first}{tag}@t.com", Email = $"{first}{tag}@t.com",
            FirstName = first, LastName = "Chat", Initials = "CC", AvatarColor = "#888",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Channel_message_and_thread_reply_save_without_a_recipient()
    {
        await using var db = fixture.CreateContext();
        var ada = await AddUserAsync(db, "Ada");
        var ben = await AddUserAsync(db, "Ben");
        var room = new ChatRoom { Name = $"floor-{Guid.NewGuid():N}"[..20], IsGroup = true, CreatedById = ada.Id };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();
        db.ChatRoomMembers.AddRange(
            new ChatRoomMember { ChatRoomId = room.Id, UserId = ada.Id },
            new ChatRoomMember { ChatRoomId = room.Id, UserId = ben.Id });
        await db.SaveChangesAsync();

        var sent = await new SendChatRoomMessageHandler(db, Hub())
            .Handle(new SendChatRoomMessageCommand(ada.Id, room.Id, "Press 4 is down"), CancellationToken.None);
        var reply = await new ReplyInThreadHandler(db, Hub())
            .Handle(new ReplyInThreadCommand(ben.Id, sent.Id, "On it"), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var rows = await verify.ChatMessages.AsNoTracking()
            .Where(m => m.ChatRoomId == room.Id).OrderBy(m => m.Id).ToListAsync();
        rows.Select(r => r.Id).Should().Equal(sent.Id, reply.Id);
        rows.Should().OnlyContain(r => r.RecipientId == null);

        var conversations = await new GetConversationsHandler(verify)
            .Handle(new GetConversationsQuery(ada.Id), CancellationToken.None);
        conversations.Should().BeEmpty();
    }
}
