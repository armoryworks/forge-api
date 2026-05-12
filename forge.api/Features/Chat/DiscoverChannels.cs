using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record DiscoverChannelsQuery(int UserId, string? Search) : IRequest<List<ChatRoomResponseModel>>;

public class DiscoverChannelsHandler(AppDbContext db) : IRequestHandler<DiscoverChannelsQuery, List<ChatRoomResponseModel>>
{
    public async Task<List<ChatRoomResponseModel>> Handle(DiscoverChannelsQuery request, CancellationToken ct)
    {
        var query = db.Set<ChatRoom>()
            .AsNoTracking()
            .Include(r => r.Members)
            .Where(r => r.ChannelType == ChannelType.Custom)
            .Where(r => !r.Members.Any(m => m.UserId == request.UserId));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(search)
                || (r.Description != null && r.Description.ToLower().Contains(search)));
        }

        var rooms = await query
            .OrderBy(r => r.Name)
            .Take(50)
            .ToListAsync(ct);

        return rooms.Select(r => new ChatRoomResponseModel(
            r.Id,
            r.Name,
            r.IsGroup,
            r.CreatedById,
            r.CreatedAt,
            new List<ChatRoomMemberResponseModel>(),
            r.ChannelType,
            r.Description,
            r.TeamId,
            r.IsReadOnly,
            r.IconName,
            0,
            null,
            null)).ToList();
    }
}
