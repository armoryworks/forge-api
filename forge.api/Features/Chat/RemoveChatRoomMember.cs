using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record RemoveChatRoomMemberCommand(int RoomId, int UserId, int RequesterId) : IRequest;

public class RemoveChatRoomMemberHandler(AppDbContext db) : IRequestHandler<RemoveChatRoomMemberCommand>
{
    public async Task Handle(RemoveChatRoomMemberCommand request, CancellationToken ct)
    {
        var room = await db.Set<Core.Entities.ChatRoom>()
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == request.RoomId, ct)
            ?? throw new KeyNotFoundException($"Chat room {request.RoomId} not found.");

        if (room.Members.All(m => m.UserId != request.RequesterId))
            throw new UnauthorizedAccessException("You are not a member of this chat room.");

        var member = room.Members.FirstOrDefault(m => m.UserId == request.UserId);
        if (member == null) return;

        db.Set<Core.Entities.ChatRoomMember>().Remove(member);
        await db.SaveChangesAsync(ct);
    }
}
