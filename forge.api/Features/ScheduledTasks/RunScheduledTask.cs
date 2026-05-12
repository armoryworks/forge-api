using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record RunScheduledTaskCommand(int Id) : IRequest<int>;

public class RunScheduledTaskHandler(AppDbContext db) : IRequestHandler<RunScheduledTaskCommand, int>
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

        // Generate job number
        var jobCount = await db.Jobs.CountAsync(ct);
        var jobNumber = $"JOB-{(jobCount + 1):D5}";

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

        db.Jobs.Add(job);

        task.LastRunAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return job.Id;
    }
}
