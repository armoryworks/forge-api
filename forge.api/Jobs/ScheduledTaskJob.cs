using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.ScheduledTasks;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class ScheduledTaskJob(AppDbContext db, IMediator mediator, IClock clock, ILogger<ScheduledTaskJob> logger)
{
    public async Task RunDueTasksAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var dueTasks = await db.ScheduledTasks
            .Where(t => t.IsActive && t.DeletedAt == null && t.NextRunAt <= now)
            .ToListAsync(ct);

        logger.LogInformation("Found {Count} scheduled tasks due for execution", dueTasks.Count);

        foreach (var task in dueTasks)
        {
            try
            {
                var jobId = await mediator.Send(new RunScheduledTaskCommand(task.Id), ct);
                logger.LogInformation("Scheduled task {TaskId} ({Name}) created job {JobId}", task.Id, task.Name, jobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to run scheduled task {TaskId} ({Name})", task.Id, task.Name);
            }
        }
    }
}
