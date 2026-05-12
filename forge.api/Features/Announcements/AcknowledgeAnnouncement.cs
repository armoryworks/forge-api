using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record AcknowledgeAnnouncementCommand(int AnnouncementId, int UserId) : IRequest;

public class AcknowledgeAnnouncementHandler(AppDbContext db) : IRequestHandler<AcknowledgeAnnouncementCommand>
{
    public async Task Handle(AcknowledgeAnnouncementCommand request, CancellationToken ct)
    {
        var announcement = await db.Announcements.FindAsync([request.AnnouncementId], ct)
            ?? throw new KeyNotFoundException($"Announcement {request.AnnouncementId} not found.");

        var exists = await db.AnnouncementAcknowledgments
            .AnyAsync(a => a.AnnouncementId == request.AnnouncementId && a.UserId == request.UserId, ct);

        if (exists) return;

        db.AnnouncementAcknowledgments.Add(new AnnouncementAcknowledgment
        {
            AnnouncementId = request.AnnouncementId,
            UserId = request.UserId,
            AcknowledgedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
