using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TrackTypes;

public record GetTrackTypeStagesQuery(int TrackTypeId) : IRequest<List<TrackTypeStageAdminResponseModel>>;

public class GetTrackTypeStagesHandler(AppDbContext db)
    : IRequestHandler<GetTrackTypeStagesQuery, List<TrackTypeStageAdminResponseModel>>
{
    public async Task<List<TrackTypeStageAdminResponseModel>> Handle(GetTrackTypeStagesQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.TrackTypes.AsNoTracking()
            .AnyAsync(t => t.Id == request.TrackTypeId && t.IsActive, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Track type with ID {request.TrackTypeId} not found.");

        return await db.JobStages.AsNoTracking()
            .Where(s => s.TrackTypeId == request.TrackTypeId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new TrackTypeStageAdminResponseModel(
                s.Id,
                s.Name,
                s.Code,
                s.SortOrder,
                s.Color,
                s.WIPLimit,
                s.IsIrreversible,
                s.IsMandatory,
                s.AccountingDocumentType != null ? s.AccountingDocumentType.ToString() : null,
                s.IsActive,
                db.Jobs.Count(j => j.CurrentStageId == s.Id && !j.IsArchived)))
            .ToListAsync(cancellationToken);
    }
}
