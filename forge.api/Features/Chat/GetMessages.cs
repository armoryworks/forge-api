using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetMessagesQuery(int UserId, int OtherUserId, int Page = 1, int PageSize = 50) : IRequest<List<ChatMessageResponseModel>>;

public class GetMessagesHandler(AppDbContext db)
    : IRequestHandler<GetMessagesQuery, List<ChatMessageResponseModel>>
{
    public async Task<List<ChatMessageResponseModel>> Handle(GetMessagesQuery request, CancellationToken ct)
    {
        var messages = await db.ChatMessages
            .AsNoTracking()
            .Where(m => (m.SenderId == request.UserId && m.RecipientId == request.OtherUserId)
                || (m.SenderId == request.OtherUserId && m.RecipientId == request.UserId))
            .OrderByDescending(m => m.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Join(db.Users, m => m.SenderId, u => u.Id, (m, u) => new { m, u })
            .Select(x => new ChatMessageResponseModel(
                x.m.Id,
                x.m.SenderId,
                (x.u.FirstName + " " + x.u.LastName).Trim(),
                x.u.Initials ?? "??",
                x.u.AvatarColor ?? "#94a3b8",
                x.m.RecipientId,
                x.m.Content,
                x.m.IsRead,
                x.m.CreatedAt,
                x.m.ParentMessageId,
                x.m.ThreadReplyCount,
                x.m.ThreadLastReplyAt,
                x.m.Mentions.Select(mm => new ChatMessageMentionResponseModel(
                    mm.EntityType, mm.EntityId, mm.DisplayText)).ToList(),
                x.m.FileAttachment != null
                    ? new ChatFileAttachmentResponseModel(
                        x.m.FileAttachment.Id,
                        x.m.FileAttachment.FileName,
                        x.m.FileAttachment.ContentType,
                        x.m.FileAttachment.Size)
                    : null,
                x.m.LinkedEntityType,
                x.m.LinkedEntityId))
            .ToListAsync(ct);

        // Mark unread messages as read
        var unreadIds = await db.ChatMessages
            .Where(m => m.SenderId == request.OtherUserId && m.RecipientId == request.UserId && !m.IsRead)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (unreadIds.Count > 0)
        {
            await db.ChatMessages
                .Where(m => unreadIds.Contains(m.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsRead, true)
                    .SetProperty(m => m.ReadAt, DateTimeOffset.UtcNow), ct);
        }

        messages.Reverse();
        return messages;
    }
}
