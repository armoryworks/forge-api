using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record CreateAnnouncementCommand(
    int CreatedById,
    string Title,
    string Content,
    AnnouncementSeverity Severity,
    AnnouncementScope Scope,
    bool RequiresAcknowledgment,
    DateTimeOffset? ExpiresAt,
    int? DepartmentId,
    List<int>? TargetTeamIds,
    int? TemplateId) : IRequest<AnnouncementResponseModel>;

public class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.TargetTeamIds)
            .NotEmpty()
            .When(x => x.Scope is AnnouncementScope.SelectedTeams or AnnouncementScope.IndividualTeam)
            .WithMessage("Target teams are required for team-scoped announcements.");
        RuleFor(x => x.DepartmentId)
            .NotNull()
            .When(x => x.Scope == AnnouncementScope.Department)
            .WithMessage("Department is required for department-scoped announcements.");
    }
}

public class CreateAnnouncementHandler(
    AppDbContext db,
    IHubContext<ChatHub> chatHub) : IRequestHandler<CreateAnnouncementCommand, AnnouncementResponseModel>
{
    public async Task<AnnouncementResponseModel> Handle(CreateAnnouncementCommand request, CancellationToken ct)
    {
        var announcement = new Announcement
        {
            Title = request.Title,
            Content = request.Content,
            Severity = request.Severity,
            Scope = request.Scope,
            RequiresAcknowledgment = request.RequiresAcknowledgment,
            ExpiresAt = request.ExpiresAt,
            DepartmentId = request.DepartmentId,
            TemplateId = request.TemplateId,
            CreatedById = request.CreatedById,
        };

        if (request.TargetTeamIds is { Count: > 0 })
        {
            foreach (var teamId in request.TargetTeamIds.Distinct())
            {
                announcement.TargetTeams.Add(new AnnouncementTeam { TeamId = teamId });
            }
        }

        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(ct);

        var creator = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.CreatedById)
            .Select(u => (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync(ct) ?? "System";

        var response = new AnnouncementResponseModel(
            announcement.Id,
            announcement.Title,
            announcement.Content,
            announcement.Severity,
            announcement.Scope,
            announcement.RequiresAcknowledgment,
            announcement.ExpiresAt,
            announcement.IsSystemGenerated,
            announcement.SystemSource,
            announcement.CreatedById,
            creator,
            announcement.CreatedAt,
            0,
            0,
            false,
            request.TargetTeamIds ?? []);

        // Broadcast via SignalR based on scope
        await BroadcastAnnouncement(response, request.TargetTeamIds, ct);

        return response;
    }

    private async Task BroadcastAnnouncement(AnnouncementResponseModel announcement, List<int>? targetTeamIds, CancellationToken ct)
    {
        // ChatHub.OnConnectedAsync adds each user to `user:{id}` on connect.
        // For scoped announcements, resolve recipient user IDs and push per-user so
        // every recipient receives the event regardless of which page they're on.
        switch (announcement.Scope)
        {
            case AnnouncementScope.CompanyWide:
            case AnnouncementScope.Department:
                // Department is currently treated as company-wide (no Department entity yet).
                await chatHub.Clients.All.SendAsync("announcementReceived", announcement, ct);
                break;

            case AnnouncementScope.SelectedTeams:
            case AnnouncementScope.IndividualTeam:
                if (targetTeamIds is { Count: > 0 })
                {
                    var teamUserIds = await db.Users
                        .AsNoTracking()
                        .Where(u => u.TeamId.HasValue && targetTeamIds.Contains(u.TeamId!.Value))
                        .Select(u => u.Id)
                        .ToListAsync(ct);
                    if (teamUserIds.Count > 0)
                    {
                        var groups = teamUserIds.Select(id => $"user:{id}").ToList();
                        await chatHub.Clients.Groups(groups).SendAsync("announcementReceived", announcement, ct);
                    }
                }
                break;

            case AnnouncementScope.TeamLeadsOnly:
                // Mirror GetActiveAnnouncements filter: team leads = Manager or Admin.
                var leadIds = await (from ur in db.UserRoles
                                     join r in db.Roles on ur.RoleId equals r.Id
                                     where r.Name == "Manager" || r.Name == "Admin"
                                     select ur.UserId).Distinct().ToListAsync(ct);
                if (leadIds.Count > 0)
                {
                    var groups = leadIds.Select(id => $"user:{id}").ToList();
                    await chatHub.Clients.Groups(groups).SendAsync("announcementReceived", announcement, ct);
                }
                break;
        }
    }
}
