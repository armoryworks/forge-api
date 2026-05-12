using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetChatRoomsQuery(int UserId) : IRequest<List<ChatRoomResponseModel>>;

public class GetChatRoomsHandler(AppDbContext db) : IRequestHandler<GetChatRoomsQuery, List<ChatRoomResponseModel>>
{
    public async Task<List<ChatRoomResponseModel>> Handle(GetChatRoomsQuery request, CancellationToken ct)
    {
        var rooms = await db.Set<Core.Entities.ChatRoom>()
            .AsNoTracking()
            .Include(r => r.Members)
            .Where(r => r.Members.Any(m => m.UserId == request.UserId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var allUserIds = rooms.SelectMany(r => r.Members.Select(m => m.UserId)).Distinct().ToList();
        var users = await db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return rooms.Select(r => new ChatRoomResponseModel(
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
            r.IconName)).ToList();
    }
}
