using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetPartRevisionsQuery(int PartId) : IRequest<List<PartRevisionResponseModel>>;

public class GetPartRevisionsHandler(AppDbContext db) : IRequestHandler<GetPartRevisionsQuery, List<PartRevisionResponseModel>>
{
    public async Task<List<PartRevisionResponseModel>> Handle(GetPartRevisionsQuery request, CancellationToken cancellationToken)
    {
        return await db.PartRevisions
            .Where(r => r.PartId == request.PartId)
            .OrderByDescending(r => r.EffectiveDate)
            .Select(r => new PartRevisionResponseModel(
                r.Id,
                r.PartId,
                r.Revision,
                r.ChangeDescription,
                r.ChangeReason,
                r.EffectiveDate,
                r.IsCurrent,
                r.Files.Count,
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
