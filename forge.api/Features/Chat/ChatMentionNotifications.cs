using System.Text.RegularExpressions;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public static class ChatMentionNotifications
{
    public static async Task NotifyAsync(
        AppDbContext db, ISender? sender, ChatMessage message, IEnumerable<ChatMessageMention> mentions,
        string senderName, CancellationToken ct)
    {
        if (sender is null)
            return;

        var mentionedIds = mentions
            .Where(m => string.Equals(m.EntityType, "user", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.EntityId)
            .Where(id => id != message.SenderId)
            .Distinct()
            .ToList();
        if (mentionedIds.Count == 0)
            return;

        List<int> recipients;
        if (message.ChatRoomId is int roomId)
        {
            recipients = await db.Set<ChatRoomMember>()
                .Where(m => m.ChatRoomId == roomId && mentionedIds.Contains(m.UserId))
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            recipients = mentionedIds.Where(id => id == message.RecipientId).ToList();
        }

        var text = MentionText(message.Content);
        var snippet = text.Length > 200 ? text[..200] + "..." : text;
        foreach (var userId in recipients)
        {
            await sender.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: userId,
                Type: "mention",
                Severity: "info",
                Source: "user",
                Title: $"{senderName} mentioned you in chat",
                Message: snippet,
                EntityType: message.ChatRoomId.HasValue ? "ChatRoom" : "ChatMessage",
                EntityId: message.ChatRoomId ?? message.Id,
                SenderId: message.SenderId)), ct);
        }
    }

    private static string MentionText(string content) =>
        Regex.Replace(content, @"@\[\w+:\d+:([^\]]+)\]", "@$1");
}
