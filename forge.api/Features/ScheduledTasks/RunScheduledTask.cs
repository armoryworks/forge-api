using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record RunScheduledTaskCommand(int Id) : IRequest<int>;

public class RunScheduledTaskHandler(AppDbContext db, IJobRepository jobRepo, IClock clock) : IRequestHandler<RunScheduledTaskCommand, int>
{
    public async Task<int> Handle(RunScheduledTaskCommand request, CancellationToken ct)
    {
        var task = await db.ScheduledTasks
            .Include(t => t.TrackType)
            .ThenInclude(tt => tt.Stages)
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Scheduled task {request.Id} not found.");

        var firstStage = task.TrackType.Stages.OrderBy(s => s.SortOrder).FirstOrDefault()
            ?? throw new InvalidOperationException("Track type has no stages.");

        var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = task.Name,
            Description = task.Description,
            TrackTypeId = task.TrackTypeId,
            CurrentStageId = firstStage.Id,
            AssigneeId = task.AssigneeId,
            IsInternal = true,
            InternalProjectTypeId = task.InternalProjectTypeId,
        };
        job.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.Created,
            Description = $"Job {jobNumber} created from scheduled task {task.Name}.",
        });

        db.Jobs.Add(job);

        task.LastRunAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return job.Id;
    }
}
