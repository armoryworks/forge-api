using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record SendChatRoomMessageCommand(
    int SenderId,
    int RoomId,
    string Content,
    int? FileAttachmentId = null,
    string? LinkedEntityType = null,
    int? LinkedEntityId = null) : IRequest<ChatMessageResponseModel>;

public class SendChatRoomMessageValidator : AbstractValidator<SendChatRoomMessageCommand>
{
    public SendChatRoomMessageValidator()
    {
        RuleFor(x => x.RoomId).GreaterThan(0);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}

public class SendChatRoomMessageHandler(AppDbContext db, IHubContext<ChatHub> chatHub)
    : IRequestHandler<SendChatRoomMessageCommand, ChatMessageResponseModel>
{
    public async Task<ChatMessageResponseModel> Handle(SendChatRoomMessageCommand request, CancellationToken ct)
    {
        // Verify membership
        var isMember = await db.Set<ChatRoomMember>()
            .AnyAsync(m => m.ChatRoomId == request.RoomId && m.UserId == request.SenderId, ct);
        if (!isMember)
            throw new UnauthorizedAccessException("You are not a member of this chat room.");

        var sender = await db.Users.FindAsync([request.SenderId], ct)
            ?? throw new KeyNotFoundException($"User {request.SenderId} not found");

        var message = new ChatMessage
        {
            SenderId = request.SenderId,
            RecipientId = 0, // Room messages have no single recipient
            ChatRoomId = request.RoomId,
            Content = request.Content,
            FileAttachmentId = request.FileAttachmentId,
            LinkedEntityType = request.LinkedEntityType,
            LinkedEntityId = request.LinkedEntityId,
        };

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);

        // Parse and save mentions
        var mentions = MentionParser.ParseMentions(request.Content, message.Id);
        if (mentions.Count > 0)
        {
            db.ChatMessageMentions.AddRange(mentions);
            await db.SaveChangesAsync(ct);
        }

        var senderName = (sender.FirstName + " " + sender.LastName).Trim();
        var mentionModels = mentions.Select(m =>
            new ChatMessageMentionResponseModel(m.EntityType, m.EntityId, m.DisplayText)).ToList();

        var response = new ChatMessageResponseModel(
            message.Id,
            message.SenderId,
            senderName,
            sender.Initials ?? "??",
            sender.AvatarColor ?? "#94a3b8",
            0,
            message.Content,
            false,
            message.CreatedAt,
            Mentions: mentionModels);

        // Broadcast to all room members via SignalR
        var memberIds = await db.Set<ChatRoomMember>()
            .Where(m => m.ChatRoomId == request.RoomId && m.UserId != request.SenderId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        foreach (var memberId in memberIds)
        {
            await chatHub.Clients.Group($"user:{memberId}")
                .SendAsync("roomMessageReceived", new
                {
                    roomId = request.RoomId,
                    message = new ChatMessageEvent(
                        message.Id,
                        message.SenderId,
                        senderName,
                        sender.Initials ?? "??",
                        sender.AvatarColor ?? "#94a3b8",
                        0,
                        message.Content,
                        message.CreatedAt),
                }, ct);
        }

        return response;
    }
}
