using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record GetAllAnnouncementsQuery : IRequest<List<AnnouncementResponseModel>>;

public class GetAllAnnouncementsHandler(AppDbContext db) : IRequestHandler<GetAllAnnouncementsQuery, List<AnnouncementResponseModel>>
{
    public async Task<List<AnnouncementResponseModel>> Handle(GetAllAnnouncementsQuery request, CancellationToken ct)
    {
        var announcements = await db.Announcements
            .AsNoTracking()
            .Include(a => a.TargetTeams)
            .Include(a => a.Acknowledgments)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var creatorIds = announcements.Select(a => a.CreatedById).Distinct().ToList();
        var creators = await db.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);

        return announcements.Select(a => new AnnouncementResponseModel(
            a.Id,
            a.Title,
            a.Content,
            a.Severity,
            a.Scope,
            a.RequiresAcknowledgment,
            a.ExpiresAt,
            a.IsSystemGenerated,
            a.SystemSource,
            a.CreatedById,
            creators.GetValueOrDefault(a.CreatedById, "System"),
            a.CreatedAt,
            a.Acknowledgments.Count,
            0,
            false,
            a.TargetTeams.Select(t => t.TeamId).ToList()
        )).ToList();
    }
}
