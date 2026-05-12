using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record SendMessageCommand(
    int SenderId,
    int RecipientId,
    string Content,
    int? FileAttachmentId = null,
    string? LinkedEntityType = null,
    int? LinkedEntityId = null) : IRequest<ChatMessageResponseModel>;

public class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.RecipientId).GreaterThan(0);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}

public class SendMessageHandler(AppDbContext db, IHubContext<ChatHub> chatHub)
    : IRequestHandler<SendMessageCommand, ChatMessageResponseModel>
{
    public async Task<ChatMessageResponseModel> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var sender = await db.Users.FindAsync([request.SenderId], ct)
            ?? throw new KeyNotFoundException($"User {request.SenderId} not found");

        _ = await db.Users.FindAsync([request.RecipientId], ct)
            ?? throw new KeyNotFoundException($"User {request.RecipientId} not found");

        var message = new ChatMessage
        {
            SenderId = request.SenderId,
            RecipientId = request.RecipientId,
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
            message.RecipientId,
            message.Content,
            false,
            message.CreatedAt,
            Mentions: mentionModels);

        // Push to recipient via SignalR
        await chatHub.Clients.Group($"user:{request.RecipientId}")
            .SendAsync("messageReceived", new ChatMessageEvent(
                message.Id,
                message.SenderId,
                senderName,
                sender.Initials ?? "??",
                sender.AvatarColor ?? "#94a3b8",
                message.RecipientId,
                message.Content,
                message.CreatedAt), ct);

        return response;
    }
}
