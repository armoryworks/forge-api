using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record AddChatRoomMemberCommand(int RoomId, int UserId, int RequesterId) : IRequest;

public class AddChatRoomMemberHandler(AppDbContext db) : IRequestHandler<AddChatRoomMemberCommand>
{
    public async Task Handle(AddChatRoomMemberCommand request, CancellationToken ct)
    {
        var room = await db.Set<ChatRoom>()
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == request.RoomId, ct)
            ?? throw new KeyNotFoundException($"Chat room {request.RoomId} not found.");

        if (room.Members.All(m => m.UserId != request.RequesterId))
            throw new UnauthorizedAccessException("You are not a member of this chat room.");

        if (room.Members.Any(m => m.UserId == request.UserId))
            return; // Already a member

        room.Members.Add(new ChatRoomMember
        {
            ChatRoomId = request.RoomId,
            UserId = request.UserId,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
