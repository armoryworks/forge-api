using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Activity;

public record GetEntityNotesQuery(string EntityType, int EntityId) : IRequest<List<EntityNoteResponseModel>>;

public class GetEntityNotesHandler(AppDbContext db)
    : IRequestHandler<GetEntityNotesQuery, List<EntityNoteResponseModel>>
{
    public async Task<List<EntityNoteResponseModel>> Handle(GetEntityNotesQuery request, CancellationToken ct)
    {
        var notes = await db.EntityNotes
            .Where(n => n.EntityType == request.EntityType
                && n.EntityId == request.EntityId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

        var userIds = notes
            .Where(n => n.CreatedBy.HasValue)
            .Select(n => n.CreatedBy!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct)
            : new Dictionary<int, ApplicationUser>();

        return notes.Select(n =>
        {
            var user = n.CreatedBy.HasValue && users.TryGetValue(n.CreatedBy.Value, out var u) ? u : null;
            return new EntityNoteResponseModel(
                n.Id,
                n.Text,
                user is not null ? $"{user.LastName}, {user.FirstName}".Trim(',', ' ') : "Unknown",
                user?.Initials ?? "?",
                user?.AvatarColor ?? "#0d9488",
                n.CreatedAt,
                n.UpdatedAt == n.CreatedAt ? null : n.UpdatedAt);
        }).ToList();
    }
}
