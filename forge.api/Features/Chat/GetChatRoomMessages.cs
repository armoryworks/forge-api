using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetChatRoomMessagesQuery(int UserId, int RoomId, int Page, int PageSize) : IRequest<List<ChatMessageResponseModel>>;

public class GetChatRoomMessagesHandler(AppDbContext db) : IRequestHandler<GetChatRoomMessagesQuery, List<ChatMessageResponseModel>>
{
    public async Task<List<ChatMessageResponseModel>> Handle(GetChatRoomMessagesQuery request, CancellationToken ct)
    {
        // Verify user is member of room
        var isMember = await db.Set<Core.Entities.ChatRoomMember>()
            .AnyAsync(m => m.ChatRoomId == request.RoomId && m.UserId == request.UserId, ct);

        if (!isMember)
            throw new UnauthorizedAccessException("You are not a member of this chat room.");

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.ChatRoomId == request.RoomId)
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

        messages.Reverse();
        return messages;
    }
}
