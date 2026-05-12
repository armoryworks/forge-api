using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record MarkMessagesReadCommand(int UserId, int OtherUserId) : IRequest;

public class MarkMessagesReadHandler(AppDbContext db) : IRequestHandler<MarkMessagesReadCommand>
{
    public async Task Handle(MarkMessagesReadCommand request, CancellationToken ct)
    {
        await db.ChatMessages
            .Where(m => m.SenderId == request.OtherUserId && m.RecipientId == request.UserId && !m.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsRead, true)
                .SetProperty(m => m.ReadAt, DateTimeOffset.UtcNow), ct);
    }
}
