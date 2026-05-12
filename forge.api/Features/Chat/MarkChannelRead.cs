using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record MarkChannelReadCommand(int UserId, int ChannelId) : IRequest;

public class MarkChannelReadHandler(AppDbContext db) : IRequestHandler<MarkChannelReadCommand>
{
    public async Task Handle(MarkChannelReadCommand request, CancellationToken ct)
    {
        var member = await db.Set<ChatRoomMember>()
            .FirstOrDefaultAsync(m => m.ChatRoomId == request.ChannelId && m.UserId == request.UserId, ct)
            ?? throw new KeyNotFoundException("You are not a member of this channel.");

        // Get the latest message in this channel
        var lastMessageId = await db.ChatMessages
            .Where(m => m.ChatRoomId == request.ChannelId)
            .OrderByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);

        if (lastMessageId.HasValue)
        {
            member.LastReadMessageId = lastMessageId.Value;
            await db.SaveChangesAsync(ct);
        }
    }
}
