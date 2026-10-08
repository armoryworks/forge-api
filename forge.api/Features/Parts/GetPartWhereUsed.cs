using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Parents whose current BOM lists this part, ordered by parent part number, each with the
/// number of open work orders building that parent.
/// </summary>
public record GetPartWhereUsedQuery(int PartId) : IRequest<List<PartWhereUsedResponseModel>>;

public class GetPartWhereUsedHandler(AppDbContext db)
    : IRequestHandler<GetPartWhereUsedQuery, List<PartWhereUsedResponseModel>>
{
    public async Task<List<PartWhereUsedResponseModel>> Handle(GetPartWhereUsedQuery request, CancellationToken ct)
    {
        var partExists = await db.Parts.AsNoTracking().AnyAsync(p => p.Id == request.PartId, ct);
        if (!partExists)
            throw new KeyNotFoundException($"Part {request.PartId} not found");

        var rows = await db.BOMLines
            .AsNoTracking()
            .Where(b => b.ChildPartId == request.PartId && b.ParentPart.DeletedAt == null)
            .Select(b => new
            {
                b.Id,
                b.ParentPartId,
                b.ParentPart.PartNumber,
                b.ParentPart.Name,
                b.ParentPart.Revision,
                b.Quantity,
                b.SourceType,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return [];

        var parentIds = rows.Select(r => r.ParentPartId).Distinct().ToList();
        var openCounts = await db.Jobs
            .AsNoTracking()
            .Where(j => j.PartId != null && parentIds.Contains(j.PartId.Value)
                && !j.IsArchived && j.CompletedDate == null && j.Disposition == null)
            .GroupBy(j => j.PartId!.Value)
            .Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PartId, x => x.Count, ct);

        return rows
            .OrderBy(r => r.PartNumber)
            .ThenBy(r => r.Id)
            .Select(r => new PartWhereUsedResponseModel(
                r.Id,
                r.ParentPartId,
                r.PartNumber,
                r.Name,
                r.Revision,
                r.Quantity,
                r.SourceType,
                openCounts.GetValueOrDefault(r.ParentPartId)))
            .ToList();
    }
}
