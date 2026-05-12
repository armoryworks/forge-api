using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

/// <summary>
/// Phase 3 H4 / WU-20 — return the BOM revision that was pinned to the
/// job at release time, with a flag indicating whether the part's current
/// revision has advanced since.
/// </summary>
public record GetJobBomAtReleaseQuery(int JobId) : IRequest<JobBomAtReleaseResponseModel>;

public class GetJobBomAtReleaseHandler(AppDbContext db) : IRequestHandler<GetJobBomAtReleaseQuery, JobBomAtReleaseResponseModel>
{
    public async Task<JobBomAtReleaseResponseModel> Handle(GetJobBomAtReleaseQuery request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.Id, j.PartId, j.BomRevisionIdAtRelease })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        // Pull the pinned revision (if any) and the part's current revision
        // in one round-trip to determine the staleness flag.
        int? pinnedRevNumber = null;
        DateTimeOffset? effectiveDate = null;
        if (job.BomRevisionIdAtRelease.HasValue)
        {
            var pinned = await db.BomRevisions
                .Where(r => r.Id == job.BomRevisionIdAtRelease.Value)
                .Select(r => new { r.RevisionNumber, r.EffectiveDate })
                .FirstOrDefaultAsync(cancellationToken);
            if (pinned is not null)
            {
                pinnedRevNumber = pinned.RevisionNumber;
                effectiveDate = pinned.EffectiveDate;
            }
        }

        int? currentRevId = null;
        int? currentRevNumber = null;
        if (job.PartId.HasValue)
        {
            var partInfo = await db.Parts
                .Where(p => p.Id == job.PartId.Value)
                .Select(p => new { p.CurrentBomRevisionId })
                .FirstOrDefaultAsync(cancellationToken);
            if (partInfo?.CurrentBomRevisionId is int crId)
            {
                currentRevId = crId;
                currentRevNumber = await db.BomRevisions
                    .Where(r => r.Id == crId)
                    .Select(r => (int?)r.RevisionNumber)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        var stale = job.BomRevisionIdAtRelease.HasValue
            && currentRevId.HasValue
            && currentRevId.Value != job.BomRevisionIdAtRelease.Value;

        return new JobBomAtReleaseResponseModel(
            job.Id,
            job.PartId,
            job.BomRevisionIdAtRelease,
            pinnedRevNumber,
            effectiveDate,
            stale,
            currentRevId,
            currentRevNumber);
    }
}
