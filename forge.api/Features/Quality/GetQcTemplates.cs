using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetQcTemplatesQuery : IRequest<List<QcTemplateResponseModel>>;

public class GetQcTemplatesHandler(AppDbContext db)
    : IRequestHandler<GetQcTemplatesQuery, List<QcTemplateResponseModel>>
{
    public async Task<List<QcTemplateResponseModel>> Handle(
        GetQcTemplatesQuery request, CancellationToken cancellationToken)
    {
        return await db.QcChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Part)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new QcTemplateResponseModel(
                t.Id,
                t.Name,
                t.Description,
                t.PartId,
                t.Part != null ? t.Part.PartNumber : null,
                t.IsActive,
                t.Items.OrderBy(i => i.SortOrder).Select(i => new QcTemplateItemModel(
                    i.Id,
                    i.Description,
                    i.Specification,
                    i.SortOrder,
                    i.IsRequired
                )).ToList()))
            .ToListAsync(cancellationToken);
    }
}
