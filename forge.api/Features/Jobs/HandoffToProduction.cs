using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record HandoffToProductionCommand(int RdJobId) : IRequest<int>;

public class HandoffToProductionValidator : AbstractValidator<HandoffToProductionCommand>
{
    public HandoffToProductionValidator()
    {
        RuleFor(x => x.RdJobId).GreaterThan(0);
    }
}

public class HandoffToProductionHandler(AppDbContext db, IJobRepository jobRepo) : IRequestHandler<HandoffToProductionCommand, int>
{
    public async Task<int> Handle(HandoffToProductionCommand request, CancellationToken ct)
    {
        var rdJob = await db.Jobs
            .Include(j => j.TrackType)
            .FirstOrDefaultAsync(j => j.Id == request.RdJobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.RdJobId} not found.");

        // Find Production track type
        var productionTrack = await db.TrackTypes
            .Include(t => t.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(t => t.Name.Contains("Production"), ct)
            ?? throw new KeyNotFoundException("No Production track type found.");

        var firstStage = productionTrack.Stages.FirstOrDefault()
            ?? throw new KeyNotFoundException("Production track has no stages configured.");

        var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
        var maxPos = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

        var prodJob = new Job
        {
            JobNumber = jobNumber,
            Title = $"Production: {rdJob.Title}",
            Description = $"Production handoff from R&D job {rdJob.JobNumber}.\n\nOriginal description: {rdJob.Description}",
            TrackTypeId = productionTrack.Id,
            CurrentStageId = firstStage.Id,
            CustomerId = rdJob.CustomerId,
            BoardPosition = maxPos + 1,
        };

        await jobRepo.AddAsync(prodJob, ct);

        // Create bidirectional links
        db.Set<JobLink>().Add(new JobLink
        {
            SourceJobId = rdJob.Id,
            TargetJobId = prodJob.Id,
            LinkType = JobLinkType.HandoffTo,
        });

        db.Set<JobLink>().Add(new JobLink
        {
            SourceJobId = prodJob.Id,
            TargetJobId = rdJob.Id,
            LinkType = JobLinkType.HandoffFrom,
        });

        await db.SaveChangesAsync(ct);

        return prodJob.Id;
    }
}
