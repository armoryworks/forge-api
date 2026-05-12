using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetChannelsQuery(int UserId) : IRequest<List<ChatRoomResponseModel>>;

public class GetChannelsHandler(AppDbContext db) : IRequestHandler<GetChannelsQuery, List<ChatRoomResponseModel>>
{
    public async Task<List<ChatRoomResponseModel>> Handle(GetChannelsQuery request, CancellationToken ct)
    {
        var rooms = await db.Set<ChatRoom>()
            .AsNoTracking()
            .Include(r => r.Members)
            .Where(r => r.Members.Any(m => m.UserId == request.UserId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var allUserIds = rooms.SelectMany(r => r.Members.Select(m => m.UserId)).Distinct().ToList();
        var users = await db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        // Get last message and unread counts per room
        var roomIds = rooms.Select(r => r.Id).ToList();

        var lastMessages = await db.ChatMessages
            .Where(m => m.ChatRoomId != null && roomIds.Contains(m.ChatRoomId.Value))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => new
            {
                RoomId = g.Key,
                LastMessage = g.OrderByDescending(m => m.CreatedAt).First(),
            })
            .ToDictionaryAsync(x => x.RoomId!.Value, x => x.LastMessage, ct);

        // Unread counts: messages created after member's LastReadMessageId
        var memberLookup = rooms
            .SelectMany(r => r.Members.Where(m => m.UserId == request.UserId).Select(m => new { r.Id, m.LastReadMessageId }))
            .ToDictionary(x => x.Id, x => x.LastReadMessageId);

        var unreadCounts = new Dictionary<int, int>();
        foreach (var roomId in roomIds)
        {
            var lastReadId = memberLookup.GetValueOrDefault(roomId);
            var count = await db.ChatMessages
                .CountAsync(m => m.ChatRoomId == roomId
                    && m.SenderId != request.UserId
                    && (lastReadId == null || m.Id > lastReadId), ct);
            unreadCounts[roomId] = count;
        }

        return rooms.Select(r =>
        {
            var lastMsg = lastMessages.GetValueOrDefault(r.Id);
            return new ChatRoomResponseModel(
                r.Id,
                r.Name,
                r.IsGroup,
                r.CreatedById,
                r.CreatedAt,
                r.Members.Select(m =>
                {
                    var u = users.GetValueOrDefault(m.UserId);
                    return new ChatRoomMemberResponseModel(
                        m.UserId,
                        u != null ? (u.FirstName + " " + u.LastName).Trim() : "Unknown",
                        u?.Initials ?? "??",
                        u?.AvatarColor ?? "#94a3b8",
                        m.Role,
                        m.MutedUntil.HasValue && m.MutedUntil > DateTimeOffset.UtcNow);
                }).ToList(),
                r.ChannelType,
                r.Description,
                r.TeamId,
                r.IsReadOnly,
                r.IconName,
                unreadCounts.GetValueOrDefault(r.Id),
                lastMsg?.Content,
                lastMsg?.CreatedAt);
        })
        .OrderByDescending(r => r.LastMessageAt ?? r.CreatedAt)
        .ToList();
    }
}
