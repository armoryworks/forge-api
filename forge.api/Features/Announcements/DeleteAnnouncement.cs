using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

// F-13-ANN-01: retract (soft-delete) a published announcement.
public sealed record DeleteAnnouncementCommand(int Id) : IRequest;

public sealed class DeleteAnnouncementHandler(AppDbContext db) : IRequestHandler<DeleteAnnouncementCommand>
{
    public async Task Handle(DeleteAnnouncementCommand request, CancellationToken ct)
    {
        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Announcement {request.Id} not found");

        announcement.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
