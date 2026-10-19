using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record ReplyInThreadCommand(int SenderId, int ParentMessageId, string Content) : IRequest<ChatMessageResponseModel>;

public class ReplyInThreadValidator : AbstractValidator<ReplyInThreadCommand>
{
    public ReplyInThreadValidator()
    {
        RuleFor(x => x.ParentMessageId).GreaterThan(0);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}

public class ReplyInThreadHandler(AppDbContext db, IHubContext<ChatHub> chatHub)
    : IRequestHandler<ReplyInThreadCommand, ChatMessageResponseModel>
{
    public async Task<ChatMessageResponseModel> Handle(ReplyInThreadCommand request, CancellationToken ct)
    {
        var parent = await db.ChatMessages.FindAsync([request.ParentMessageId], ct)
            ?? throw new KeyNotFoundException($"Message {request.ParentMessageId} not found");
        await ChatMessageAccess.EnsureVisibleAsync(db, parent, request.SenderId, ct);

        var sender = await db.Users.FindAsync([request.SenderId], ct)
            ?? throw new KeyNotFoundException($"User {request.SenderId} not found");

        int? dmRecipientId = parent.ChatRoomId.HasValue
            ? null
            : parent.SenderId == request.SenderId ? parent.RecipientId : parent.SenderId;

        var reply = new ChatMessage
        {
            SenderId = request.SenderId,
            RecipientId = dmRecipientId,
            ChatRoomId = parent.ChatRoomId,
            Content = request.Content,
            ParentMessageId = request.ParentMessageId,
        };

        db.ChatMessages.Add(reply);

        // Update parent thread counters
        parent.ThreadReplyCount++;
        parent.ThreadLastReplyAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Parse and save mentions
        var mentions = MentionParser.ParseMentions(request.Content, reply.Id);
        if (mentions.Count > 0)
        {
            db.ChatMessageMentions.AddRange(mentions);
            await db.SaveChangesAsync(ct);
        }

        var senderName = $"{sender.LastName}, {sender.FirstName}".Trim(',', ' ');
        var mentionModels = mentions.Select(m =>
            new ChatMessageMentionResponseModel(m.EntityType, m.EntityId, m.DisplayText)).ToList();

        var response = new ChatMessageResponseModel(
            reply.Id,
            reply.SenderId,
            senderName,
            sender.Initials ?? "??",
            sender.AvatarColor ?? "#94a3b8",
            reply.RecipientId ?? 0,
            reply.Content,
            false,
            reply.CreatedAt,
            reply.ParentMessageId,
            0,
            null,
            mentionModels);

        // Broadcast thread reply to relevant users
        if (parent.ChatRoomId.HasValue)
        {
            var memberIds = await db.ChatRoomMembers
                .Where(m => m.ChatRoomId == parent.ChatRoomId && m.UserId != request.SenderId)
                .Select(m => m.UserId)
                .ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                await chatHub.Clients.Group($"user:{memberId}")
                    .SendAsync("roomMessageReceived", new
                    {
                        roomId = parent.ChatRoomId,
                        message = new ChatMessageEvent(
                            reply.Id, reply.SenderId, senderName,
                            sender.Initials ?? "??", sender.AvatarColor ?? "#94a3b8",
                            0, reply.Content, reply.CreatedAt,
                            reply.ParentMessageId, 0),
                    }, ct);
            }
        }
        else
        {
            // DM thread reply — notify the other participant
            var recipientId = dmRecipientId ?? 0;
            await chatHub.Clients.Group($"user:{recipientId}")
                .SendAsync("messageReceived", new ChatMessageEvent(
                    reply.Id, reply.SenderId, senderName,
                    sender.Initials ?? "??", sender.AvatarColor ?? "#94a3b8",
                    recipientId, reply.Content, reply.CreatedAt,
                    reply.ParentMessageId, 0), ct);
        }

        return response;
    }
}
