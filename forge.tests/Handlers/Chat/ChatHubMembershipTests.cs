using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class ChatHubMembershipTests
{
    private const int MemberId = 11;
    private const int OutsiderId = 12;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IGroupManager> _groups = new();
    private readonly Mock<IClientProxy> _others = new();

    private async Task<int> AddRoomAsync()
    {
        var room = new ChatRoom { Name = "payroll", IsGroup = true, CreatedById = MemberId };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();
        _db.ChatRoomMembers.Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = MemberId });
        await _db.SaveChangesAsync();
        return room.Id;
    }

    private ChatHub HubFor(int userId)
    {
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns($"conn-{userId}");
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")));
        context.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(_others.Object);

        return new ChatHub(_db) { Context = context.Object, Groups = _groups.Object, Clients = clients.Object };
    }

    [Fact]
    public async Task A_non_member_cannot_join_a_room_or_its_channel_group()
    {
        var roomId = await AddRoomAsync();
        var hub = HubFor(OutsiderId);

        await hub.JoinRoom(roomId);
        await hub.JoinChannel(roomId);

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), $"chat-room:{roomId}", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_member_joins_the_room_group()
    {
        var roomId = await AddRoomAsync();

        await HubFor(MemberId).JoinChannel(roomId);

        _groups.Verify(g => g.AddToGroupAsync($"conn-{MemberId}", $"chat-room:{roomId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_non_member_cannot_send_typing_events_into_a_room()
    {
        var roomId = await AddRoomAsync();

        await HubFor(OutsiderId).StartTyping(roomId);
        await HubFor(MemberId).StartTyping(roomId);

        _others.Verify(p => p.SendCoreAsync("userTyping", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
