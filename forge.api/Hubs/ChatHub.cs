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
        if (await IsMemberAsync(roomId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-room:{roomId}");
    }

    public async Task LeaveRoom(int roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-room:{roomId}");
    }

    public async Task JoinChannel(int channelId)
    {
        if (await IsMemberAsync(channelId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-room:{channelId}");
    }

    public async Task LeaveChannel(int channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-room:{channelId}");
    }

    public async Task StartTyping(int channelId)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && await IsMemberAsync(channelId))
        {
            await Clients.OthersInGroup($"chat-room:{channelId}")
                .SendAsync("userTyping", new { channelId, userId = int.Parse(userId) });
        }
    }

    public async Task StopTyping(int channelId)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && await IsMemberAsync(channelId))
        {
            await Clients.OthersInGroup($"chat-room:{channelId}")
                .SendAsync("userStoppedTyping", new { channelId, userId = int.Parse(userId) });
        }
    }

    private async Task<bool> IsMemberAsync(int roomId)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userId, out var id)
            && await db.ChatRoomMembers.AnyAsync(m => m.ChatRoomId == roomId && m.UserId == id, Context.ConnectionAborted);
    }
}
