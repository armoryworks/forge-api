using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record DeleteAnnouncementTemplateCommand(int Id) : IRequest;

public class DeleteAnnouncementTemplateHandler(AppDbContext db) : IRequestHandler<DeleteAnnouncementTemplateCommand>
{
    public async Task Handle(DeleteAnnouncementTemplateCommand request, CancellationToken ct)
    {
        var template = await db.AnnouncementTemplates.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Announcement template {request.Id} not found.");

        db.AnnouncementTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }
}
