using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record MuteChannelCommand(int UserId, int ChannelId, bool Mute) : IRequest;

public class MuteChannelHandler(AppDbContext db) : IRequestHandler<MuteChannelCommand>
{
    public async Task Handle(MuteChannelCommand request, CancellationToken ct)
    {
        var member = await db.Set<ChatRoomMember>()
            .FirstOrDefaultAsync(m => m.ChatRoomId == request.ChannelId && m.UserId == request.UserId, ct)
            ?? throw new KeyNotFoundException("You are not a member of this channel.");

        member.MutedUntil = request.Mute
            ? DateTimeOffset.MaxValue // Mute indefinitely
            : null;

        await db.SaveChangesAsync(ct);
    }
}
