using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public static class ChatMessageAccess
{
    public static async Task EnsureVisibleAsync(AppDbContext db, ChatMessage message, int userId, CancellationToken ct)
    {
        var visible = message.ChatRoomId is int roomId
            ? await db.ChatRoomMembers.AnyAsync(m => m.ChatRoomId == roomId && m.UserId == userId, ct)
            : message.SenderId == userId || message.RecipientId == userId;

        if (!visible)
            throw new KeyNotFoundException($"Message {message.Id} not found");
    }
}
