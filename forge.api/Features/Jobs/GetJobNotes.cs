using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobNotesQuery(int JobId) : IRequest<List<JobNoteResponseModel>>;

public class GetJobNotesHandler(AppDbContext db) : IRequestHandler<GetJobNotesQuery, List<JobNoteResponseModel>>
{
    public async Task<List<JobNoteResponseModel>> Handle(GetJobNotesQuery request, CancellationToken cancellationToken)
    {
        var notes = await db.JobNotes
            .Where(n => n.JobId == request.JobId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = notes
            .Where(n => n.CreatedBy.HasValue)
            .Select(n => n.CreatedBy!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, cancellationToken)
            : new Dictionary<int, ApplicationUser>();

        return notes.Select(n =>
        {
            var user = n.CreatedBy.HasValue && users.TryGetValue(n.CreatedBy.Value, out var u) ? u : null;
            return new JobNoteResponseModel(
                n.Id,
                n.Text,
                user is not null ? $"{user.LastName}, {user.FirstName}".Trim(',', ' ') : "Unknown",
                user?.Initials ?? "?",
                user?.AvatarColor ?? "#0d9488",
                n.CreatedAt,
                n.UpdatedAt == n.CreatedAt ? null : n.UpdatedAt
            );
        }).ToList();
    }
}
