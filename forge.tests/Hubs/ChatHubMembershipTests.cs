using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Hubs;

public class ChatHubMembershipTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IGroupManager> _groups = new();
    private readonly Mock<IClientProxy> _others = new();

    private ChatHub Connect(int userId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.User).Returns(user);
        context.Setup(c => c.UserIdentifier).Returns(userId.ToString());
        context.Setup(c => c.ConnectionId).Returns("conn-1");
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(_others.Object);
        return new ChatHub(_db) { Context = context.Object, Groups = _groups.Object, Clients = clients.Object };
    }

    private async Task<(int RoomId, int MemberId, int OutsiderId)> SeedRoomAsync()
    {
        var ada = AddUser("ada");
        var eve = AddUser("eve");
        await _db.SaveChangesAsync();
        var room = new ChatRoom { Name = "sales", IsGroup = true, CreatedById = ada.Id };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();
        _db.ChatRoomMembers.Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = ada.Id });
        await _db.SaveChangesAsync();
        return (room.Id, ada.Id, eve.Id);
    }

    private ApplicationUser AddUser(string first)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first}@t.com", Email = $"{first}@t.com",
            FirstName = first, LastName = "Chat", Initials = "CC", AvatarColor = "#888",
        };
        _db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task A_member_joins_the_room_group()
    {
        var (roomId, memberId, _) = await SeedRoomAsync();

        await Connect(memberId).JoinChannel(roomId);
        await Connect(memberId).JoinRoom(roomId);

        _groups.Verify(g => g.AddToGroupAsync("conn-1", $"chat-room:{roomId}", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_non_member_is_never_added_to_the_room_group()
    {
        var (roomId, _, outsiderId) = await SeedRoomAsync();

        await Connect(outsiderId).JoinChannel(roomId);
        await Connect(outsiderId).JoinRoom(roomId);

        _groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_member_typing_reaches_the_room()
    {
        var (roomId, memberId, _) = await SeedRoomAsync();

        await Connect(memberId).StartTyping(roomId);
        await Connect(memberId).StopTyping(roomId);

        _others.Verify(p => p.SendCoreAsync("userTyping", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _others.Verify(p => p.SendCoreAsync("userStoppedTyping", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_non_member_cannot_send_typing_events_into_the_room()
    {
        var (roomId, _, outsiderId) = await SeedRoomAsync();

        await Connect(outsiderId).StartTyping(roomId);
        await Connect(outsiderId).StopTyping(roomId);

        _others.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
