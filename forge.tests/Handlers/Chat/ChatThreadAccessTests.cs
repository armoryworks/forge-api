using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Forge.Api.Features.Chat;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class ChatThreadAccessTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IHubContext<ChatHub> _hub;

    public ChatThreadAccessTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        _hub = hub.Object;
    }

    private async Task<ApplicationUser> AddUserAsync(string first)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first}@t.com", Email = $"{first}@t.com",
            FirstName = first, LastName = "Chat", Initials = "CC", AvatarColor = "#888",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<ChatMessage> AddDmAsync(int senderId, int recipientId, string content)
    {
        var message = new ChatMessage { SenderId = senderId, RecipientId = recipientId, Content = content };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task GetThread_refuses_a_user_outside_the_direct_message()
    {
        var ada = await AddUserAsync("ada");
        var ben = await AddUserAsync("ben");
        var eve = await AddUserAsync("eve");
        var dm = await AddDmAsync(ada.Id, ben.Id, "Pricing for the BFC job");

        var act = () => new GetThreadHandler(_db).Handle(new GetThreadQuery(eve.Id, dm.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetThread_refuses_a_user_outside_the_room_and_allows_a_member()
    {
        var ada = await AddUserAsync("ada");
        var eve = await AddUserAsync("eve");
        var room = new ChatRoom { Name = "sales", IsGroup = true, CreatedById = ada.Id };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();
        _db.ChatRoomMembers.Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = ada.Id });
        var parent = new ChatMessage { SenderId = ada.Id, ChatRoomId = room.Id, Content = "Margin notes" };
        _db.ChatMessages.Add(parent);
        await _db.SaveChangesAsync();

        var outsider = () => new GetThreadHandler(_db).Handle(new GetThreadQuery(eve.Id, parent.Id), CancellationToken.None);
        await outsider.Should().ThrowAsync<KeyNotFoundException>();

        var member = await new GetThreadHandler(_db).Handle(new GetThreadQuery(ada.Id, parent.Id), CancellationToken.None);
        member.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplyInThread_refuses_a_user_outside_the_direct_message()
    {
        var ada = await AddUserAsync("ada");
        var ben = await AddUserAsync("ben");
        var eve = await AddUserAsync("eve");
        var dm = await AddDmAsync(ada.Id, ben.Id, "Pricing for the BFC job");

        var act = () => new ReplyInThreadHandler(_db, _hub)
            .Handle(new ReplyInThreadCommand(eve.Id, dm.Id, "Me too"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _db.ChatMessages.Count().Should().Be(1);
    }

    [Fact]
    public async Task ReplyInThread_by_the_recipient_is_addressed_to_the_original_sender()
    {
        var ada = await AddUserAsync("ada");
        var ben = await AddUserAsync("ben");
        var dm = await AddDmAsync(ada.Id, ben.Id, "Can you check the drawing?");
        dm.IsRead = true;
        await _db.SaveChangesAsync();

        var reply = await new ReplyInThreadHandler(_db, _hub)
            .Handle(new ReplyInThreadCommand(ben.Id, dm.Id, "Rev C is fine"), CancellationToken.None);

        reply.RecipientId.Should().Be(ada.Id);
        var conversation = await new GetMessagesHandler(_db)
            .Handle(new GetMessagesQuery(ben.Id, ada.Id, 1, 50), CancellationToken.None);
        conversation.Select(m => m.Id).Should().Contain(reply.Id);
    }
}
