using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
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
        RuleFor(x => x.Content).NotEmpty().When(x => x.FileAttachmentId is null);
        RuleFor(x => x.Content).MaximumLength(4000);
    }
}

public class SendMessageHandler(AppDbContext db, IHubContext<ChatHub> chatHub, ISender? mediator = null)
    : IRequestHandler<SendMessageCommand, ChatMessageResponseModel>
{
    public async Task<ChatMessageResponseModel> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var sender = await db.Users.FindAsync([request.SenderId], ct)
            ?? throw new KeyNotFoundException($"User {request.SenderId} not found");

        _ = await db.Users.FindAsync([request.RecipientId], ct)
            ?? throw new KeyNotFoundException($"User {request.RecipientId} not found");

        FileAttachment? attachment = null;
        if (request.FileAttachmentId is int fileId)
        {
            attachment = await db.FileAttachments.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (attachment is null
                || attachment.EntityType != ChatFileRules.DirectMessageEntityType
                || attachment.EntityId != request.RecipientId
                || attachment.UploadedById != request.SenderId)
                throw new ForbiddenException("That file was not uploaded to this conversation.");
        }

        var message = new ChatMessage
        {
            SenderId = request.SenderId,
            RecipientId = request.RecipientId,
            Content = request.Content ?? string.Empty,
            FileAttachmentId = request.FileAttachmentId,
            LinkedEntityType = request.LinkedEntityType,
            LinkedEntityId = request.LinkedEntityId,
        };

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);

        var mentions = MentionParser.ParseMentions(message.Content, message.Id);
        if (mentions.Count > 0)
        {
            db.ChatMessageMentions.AddRange(mentions);
            await db.SaveChangesAsync(ct);
        }

        var senderName = $"{sender.LastName}, {sender.FirstName}".Trim(',', ' ');
        await ChatMentionNotifications.NotifyAsync(db, mediator, message, mentions, senderName, ct);
        var mentionModels = mentions.Select(m =>
            new ChatMessageMentionResponseModel(m.EntityType, m.EntityId, m.DisplayText)).ToList();

        var response = new ChatMessageResponseModel(
            message.Id,
            message.SenderId,
            senderName,
            sender.Initials ?? "??",
            sender.AvatarColor ?? "#94a3b8",
            request.RecipientId,
            message.Content,
            false,
            message.CreatedAt,
            Mentions: mentionModels,
            FileAttachment: attachment is null
                ? null
                : new ChatFileAttachmentResponseModel(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size),
            LinkedEntityType: message.LinkedEntityType,
            LinkedEntityId: message.LinkedEntityId);

        await chatHub.Clients.Group($"user:{request.RecipientId}")
            .SendAsync("messageReceived", new ChatMessageEvent(
                message.Id,
                message.SenderId,
                senderName,
                sender.Initials ?? "??",
                sender.AvatarColor ?? "#94a3b8",
                request.RecipientId,
                message.Content,
                message.CreatedAt), ct);

        return response;
    }
}
