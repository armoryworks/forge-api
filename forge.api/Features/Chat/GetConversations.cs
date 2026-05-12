using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

public record GetConversationsQuery(int UserId) : IRequest<List<ChatConversationResponseModel>>;

public class GetConversationsHandler(AppDbContext db)
    : IRequestHandler<GetConversationsQuery, List<ChatConversationResponseModel>>
{
    public async Task<List<ChatConversationResponseModel>> Handle(GetConversationsQuery request, CancellationToken ct)
    {
        var messages = await db.ChatMessages
            .Where(m => m.SenderId == request.UserId || m.RecipientId == request.UserId)
            .ToListAsync(ct);

        var userIds = messages
            .SelectMany(m => new[] { m.SenderId, m.RecipientId })
            .Where(id => id != request.UserId)
            .Distinct()
            .ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Initials, u.AvatarColor })
            .ToListAsync(ct);

        return users.Select(u =>
        {
            var convMessages = messages
                .Where(m => (m.SenderId == u.Id && m.RecipientId == request.UserId)
                    || (m.SenderId == request.UserId && m.RecipientId == u.Id))
                .OrderByDescending(m => m.CreatedAt)
                .ToList();

            var last = convMessages.FirstOrDefault();
            var unread = convMessages.Count(m => m.RecipientId == request.UserId && !m.IsRead);

            return new ChatConversationResponseModel(
                u.Id,
                u.Name,
                u.Initials ?? "??",
                u.AvatarColor ?? "#94a3b8",
                last?.Content,
                last?.CreatedAt,
                unread);
        })
        .OrderByDescending(c => c.LastMessageAt)
        .ToList();
    }
}
