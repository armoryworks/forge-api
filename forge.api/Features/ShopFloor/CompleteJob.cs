using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Quality;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record CompleteJobCommand(int JobId) : IRequest;

public class CompleteJobHandler(AppDbContext db, IClock clock) : IRequestHandler<CompleteJobCommand>
{
    public async Task Handle(CompleteJobCommand request, CancellationToken ct)
    {
        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        var lastStage = await db.JobStages
            .Where(s => s.TrackTypeId == job.TrackTypeId)
            .OrderByDescending(s => s.SortOrder)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No stages found for track type");

        var blockers = await JobQualityGate.FindBlockersAsync(db, [job.Id], ct);
        if (blockers.TryGetValue(job.Id, out var blocking))
            throw new InvalidOperationException(JobQualityGate.BlockedMessage(blocking));

        job.CurrentStageId = lastStage.Id;
        job.CompletedDate = clock.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
