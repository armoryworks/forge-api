using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Phase 3 H4 / WU-20 — list all BOM revisions for a part. Read-only.
/// </summary>
public record GetBomRevisionsQuery(int PartId) : IRequest<List<BomRevisionSummaryResponseModel>>;

public class GetBomRevisionsHandler(AppDbContext db) : IRequestHandler<GetBomRevisionsQuery, List<BomRevisionSummaryResponseModel>>
{
    public async Task<List<BomRevisionSummaryResponseModel>> Handle(GetBomRevisionsQuery request, CancellationToken cancellationToken)
    {
        var part = await db.Parts
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.Id, p.CurrentBomRevisionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var revisions = await db.BomRevisions
            .Where(r => r.PartId == request.PartId)
            .OrderByDescending(r => r.RevisionNumber)
            .Select(r => new BomRevisionSummaryResponseModel(
                r.Id,
                r.PartId,
                r.RevisionNumber,
                r.EffectiveDate,
                r.Notes,
                r.CreatedByUserId,
                r.CreatedAt,
                r.Entries.Count,
                r.Id == part.CurrentBomRevisionId))
            .ToListAsync(cancellationToken);

        return revisions;
    }
}
