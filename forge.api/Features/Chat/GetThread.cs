using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetThreadQuery(int UserId, int ParentMessageId) : IRequest<List<ChatMessageResponseModel>>;

public class GetThreadHandler(AppDbContext db)
    : IRequestHandler<GetThreadQuery, List<ChatMessageResponseModel>>
{
    public async Task<List<ChatMessageResponseModel>> Handle(GetThreadQuery request, CancellationToken ct)
    {
        var parent = await db.ChatMessages.FindAsync([request.ParentMessageId], ct)
            ?? throw new KeyNotFoundException($"Message {request.ParentMessageId} not found");

        var replies = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.ParentMessageId == request.ParentMessageId)
            .OrderBy(m => m.CreatedAt)
            .Join(db.Users, m => m.SenderId, u => u.Id, (m, u) => new { Message = m, Sender = u })
            .Select(x => new ChatMessageResponseModel(
                x.Message.Id,
                x.Message.SenderId,
                (x.Sender.FirstName + " " + x.Sender.LastName).Trim(),
                x.Sender.Initials ?? "??",
                x.Sender.AvatarColor ?? "#94a3b8",
                x.Message.RecipientId,
                x.Message.Content,
                x.Message.IsRead,
                x.Message.CreatedAt,
                x.Message.ParentMessageId,
                x.Message.ThreadReplyCount,
                x.Message.ThreadLastReplyAt,
                x.Message.Mentions.Select(mm => new ChatMessageMentionResponseModel(
                    mm.EntityType, mm.EntityId, mm.DisplayText)).ToList(),
                x.Message.FileAttachment != null
                    ? new ChatFileAttachmentResponseModel(
                        x.Message.FileAttachment.Id,
                        x.Message.FileAttachment.FileName,
                        x.Message.FileAttachment.ContentType,
                        x.Message.FileAttachment.Size)
                    : null,
                x.Message.LinkedEntityType,
                x.Message.LinkedEntityId))
            .ToListAsync(ct);

        return replies;
    }
}
