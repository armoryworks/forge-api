using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record LeaveChannelCommand(int UserId, int ChannelId) : IRequest;

public class LeaveChannelHandler(AppDbContext db) : IRequestHandler<LeaveChannelCommand>
{
    public async Task Handle(LeaveChannelCommand request, CancellationToken ct)
    {
        var room = await db.Set<ChatRoom>()
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == request.ChannelId, ct)
            ?? throw new KeyNotFoundException($"Channel {request.ChannelId} not found");

        // Cannot leave DM or system channels
        if (room.ChannelType is ChannelType.DirectMessage or ChannelType.System or ChannelType.Broadcast)
            throw new InvalidOperationException("Cannot leave this type of channel.");

        var member = room.Members.FirstOrDefault(m => m.UserId == request.UserId)
            ?? throw new KeyNotFoundException("You are not a member of this channel.");

        db.Set<ChatRoomMember>().Remove(member);

        // If owner leaves, transfer ownership to first admin or oldest member
        if (member.Role == ChannelMemberRole.Owner)
        {
            var newOwner = room.Members
                .Where(m => m.UserId != request.UserId)
                .OrderBy(m => m.Role == ChannelMemberRole.Admin ? 0 : 1)
                .ThenBy(m => m.JoinedAt)
                .FirstOrDefault();

            if (newOwner != null)
                newOwner.Role = ChannelMemberRole.Owner;
        }

        await db.SaveChangesAsync(ct);
    }
}
