using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

// F-13-ANN-01: announcements were create-only. This adds the edit path so a published
// announcement can be corrected.
public record UpdateAnnouncementCommand(int Id, UpdateAnnouncementRequestModel Data) : IRequest<AnnouncementResponseModel>;

public class UpdateAnnouncementValidator : AbstractValidator<UpdateAnnouncementCommand>
{
    public UpdateAnnouncementValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Content).NotEmpty().MaximumLength(5000);
    }
}

public class UpdateAnnouncementHandler(AppDbContext db) : IRequestHandler<UpdateAnnouncementCommand, AnnouncementResponseModel>
{
    public async Task<AnnouncementResponseModel> Handle(UpdateAnnouncementCommand request, CancellationToken ct)
    {
        var announcement = await db.Announcements
            .Include(a => a.TargetTeams)
            .FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Announcement {request.Id} not found");

        announcement.Title = request.Data.Title;
        announcement.Content = request.Data.Content;
        announcement.Severity = request.Data.Severity;
        announcement.RequiresAcknowledgment = request.Data.RequiresAcknowledgment;
        announcement.ExpiresAt = request.Data.ExpiresAt;

        await db.SaveChangesAsync(ct);

        var creator = await db.Users.AsNoTracking()
            .Where(u => u.Id == announcement.CreatedById)
            .Select(u => (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync(ct) ?? "System";

        return new AnnouncementResponseModel(
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
            announcement.TargetTeams.Select(t => t.TeamId).ToList());
    }
}
