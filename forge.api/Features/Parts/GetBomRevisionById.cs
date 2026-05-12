using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Phase 3 H4 / WU-20 — read one BOM revision in detail (immutable snapshot).
/// </summary>
public record GetBomRevisionByIdQuery(int PartId, int RevisionId) : IRequest<BomRevisionDetailResponseModel>;

public class GetBomRevisionByIdHandler(AppDbContext db) : IRequestHandler<GetBomRevisionByIdQuery, BomRevisionDetailResponseModel>
{
    public async Task<BomRevisionDetailResponseModel> Handle(GetBomRevisionByIdQuery request, CancellationToken cancellationToken)
    {
        var part = await db.Parts
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.Id, p.CurrentBomRevisionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var revision = await db.BomRevisions
            .Where(r => r.Id == request.RevisionId && r.PartId == request.PartId)
            .Include(r => r.Entries.OrderBy(e => e.SortOrder))
                .ThenInclude(e => e.Part)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"BOM revision {request.RevisionId} not found on part {request.PartId}");

        var entries = revision.Entries
            .OrderBy(e => e.SortOrder)
            .Select(e => new BomRevisionEntryResponseModel(
                e.Id,
                e.PartId,
                e.Part?.PartNumber ?? string.Empty,
                e.Part?.Description ?? string.Empty,
                e.Quantity,
                e.UnitOfMeasure,
                e.OperationId,
                e.ReferenceDesignator,
                e.SourceType,
                e.LeadTimeDays,
                e.Notes,
                e.SortOrder))
            .ToList();

        return new BomRevisionDetailResponseModel(
            revision.Id,
            revision.PartId,
            revision.RevisionNumber,
            revision.EffectiveDate,
            revision.Notes,
            revision.CreatedByUserId,
            revision.CreatedAt,
            revision.Id == part.CurrentBomRevisionId,
            entries);
    }
}
