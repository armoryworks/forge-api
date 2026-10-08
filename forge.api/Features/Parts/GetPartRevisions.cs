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
        var revisions = await db.PartRevisions
            .Where(r => r.PartId == request.PartId)
            .OrderByDescending(r => r.EffectiveDate)
            .Select(r => new
            {
                r.Id,
                r.PartId,
                r.Revision,
                r.ChangeDescription,
                r.ChangeReason,
                r.EffectiveDate,
                r.IsCurrent,
                FileCount = r.Files.Count,
                r.CreatedAt,
                r.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        var userIds = revisions
            .Where(r => r.CreatedBy.HasValue)
            .Select(r => r.CreatedBy!.Value)
            .Distinct()
            .ToList();

        var userNames = userIds.Count > 0
            ? (await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id, u => u.GetDisplayName())
            : new Dictionary<int, string>();

        return revisions.Select(r => new PartRevisionResponseModel(
            r.Id,
            r.PartId,
            r.Revision,
            r.ChangeDescription,
            r.ChangeReason,
            r.EffectiveDate,
            r.IsCurrent,
            r.FileCount,
            r.CreatedAt,
            r.CreatedBy.HasValue && userNames.TryGetValue(r.CreatedBy.Value, out var name) ? name : null))
            .ToList();
    }
}
