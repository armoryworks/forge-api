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

        var productionTrack = await db.TrackTypes
            .Include(t => t.Stages.OrderBy(s => s.SortOrder))
            .Where(t => t.IsActive && t.Id != rdJob.TrackTypeId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Code == "production" ? 0 : 1)
            .FirstOrDefaultAsync(t => t.IsDefault || t.Code == "production", ct)
            ?? throw new InvalidOperationException(
                "No production track is set. Mark one as the default in Admin > Order types.");

        var firstStage = productionTrack.Stages.Where(s => s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Order type '{productionTrack.Name}' has no statuses. Add statuses in Admin.");

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

        db.Set<JobLink>().Add(new JobLink
        {
            SourceJobId = rdJob.Id,
            TargetJob = prodJob,
            LinkType = JobLinkType.HandoffTo,
        });

        db.Set<JobLink>().Add(new JobLink
        {
            SourceJob = prodJob,
            TargetJobId = rdJob.Id,
            LinkType = JobLinkType.HandoffFrom,
        });

        prodJob.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.Created,
            Description = $"Job {jobNumber} created from the R&D handoff of job {rdJob.JobNumber}.",
        });

        rdJob.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.HandedOff,
            Description = $"Handed off to production as job {jobNumber}.",
        });

        await db.SaveChangesAsync(ct);

        return prodJob.Id;
    }
}
