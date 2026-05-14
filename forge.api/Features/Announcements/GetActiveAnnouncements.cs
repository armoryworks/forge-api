using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record GetActiveAnnouncementsQuery(int UserId) : IRequest<List<AnnouncementResponseModel>>;

public class GetActiveAnnouncementsHandler(AppDbContext db) : IRequestHandler<GetActiveAnnouncementsQuery, List<AnnouncementResponseModel>>
{
    public async Task<List<AnnouncementResponseModel>> Handle(GetActiveAnnouncementsQuery request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user == null) return [];

        var userTeamId = user.TeamId;
        var userRoles = await db.UserRoles
            .Where(ur => ur.UserId == request.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .ToListAsync(ct);

        var isTeamLead = userRoles.Any(r => r is "Manager" or "Admin");

        var now = DateTimeOffset.UtcNow;

        var announcements = await db.Announcements
            .AsNoTracking()
            .Include(a => a.TargetTeams)
            .Include(a => a.Acknowledgments)
            .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var filtered = announcements.Where(a =>
        {
            return a.Scope switch
            {
                AnnouncementScope.CompanyWide => true,
                AnnouncementScope.IndividualTeam => userTeamId.HasValue && a.TargetTeams.Any(t => t.TeamId == userTeamId.Value),
                AnnouncementScope.SelectedTeams => userTeamId.HasValue && a.TargetTeams.Any(t => t.TeamId == userTeamId.Value),
                AnnouncementScope.TeamLeadsOnly => isTeamLead,
                AnnouncementScope.Department => true, // Department scope treated as company-wide until department entity exists
                _ => false,
            };
        }).ToList();

        var creatorIds = filtered.Select(a => a.CreatedById).Distinct().ToList();
        var creators = await db.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);

        return filtered.Select(a => new AnnouncementResponseModel(
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
            a.Acknowledgments.Any(ack => ack.UserId == request.UserId),
            a.TargetTeams.Select(t => t.TeamId).ToList()
        )).ToList();
    }
}
