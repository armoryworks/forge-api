using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Announcements;

public record GetAnnouncementTemplatesQuery : IRequest<List<AnnouncementTemplateResponseModel>>;

public class GetAnnouncementTemplatesHandler(AppDbContext db) : IRequestHandler<GetAnnouncementTemplatesQuery, List<AnnouncementTemplateResponseModel>>
{
    public async Task<List<AnnouncementTemplateResponseModel>> Handle(GetAnnouncementTemplatesQuery request, CancellationToken ct)
    {
        return await db.AnnouncementTemplates
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new AnnouncementTemplateResponseModel(
                t.Id,
                t.Name,
                t.Content,
                t.DefaultSeverity,
                t.DefaultScope,
                t.DefaultRequiresAcknowledgment))
            .ToListAsync(ct);
    }
}
