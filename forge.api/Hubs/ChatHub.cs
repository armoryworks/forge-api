using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Hubs;

[Authorize]
public class ChatHub(AppDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(int roomId)
    {
        if (await MemberUserIdAsync(roomId) is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-room:{roomId}");
    }

    public async Task LeaveRoom(int roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-room:{roomId}");
    }

    public async Task JoinChannel(int channelId)
    {
        if (await MemberUserIdAsync(channelId) is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-room:{channelId}");
    }

    public async Task LeaveChannel(int channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-room:{channelId}");
    }

    public async Task StartTyping(int channelId)
    {
        if (await MemberUserIdAsync(channelId) is int userId)
        {
            await Clients.OthersInGroup($"chat-room:{channelId}")
                .SendAsync("userTyping", new { channelId, userId });
        }
    }

    public async Task StopTyping(int channelId)
    {
        if (await MemberUserIdAsync(channelId) is int userId)
        {
            await Clients.OthersInGroup($"chat-room:{channelId}")
                .SendAsync("userStoppedTyping", new { channelId, userId });
        }
    }

    private async Task<int?> MemberUserIdAsync(int roomId)
    {
        if (!int.TryParse(Context.UserIdentifier, out var userId))
            return null;

        var member = await db.ChatRoomMembers
            .AnyAsync(m => m.ChatRoomId == roomId && m.UserId == userId, Context.ConnectionAborted);
        return member ? userId : null;
    }
}
