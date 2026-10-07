using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.TimeTracking;

public record ResumeStoppedTimerCommand(int UserId, DateTimeOffset StoppedAt) : IRequest<TimeEntryResponseModel?>;

public class ResumeStoppedTimerHandler(
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHubContext<TimerHub> timerHub) : IRequestHandler<ResumeStoppedTimerCommand, TimeEntryResponseModel?>
{
    public async Task<TimeEntryResponseModel?> Handle(ResumeStoppedTimerCommand request, CancellationToken cancellationToken)
    {
        if (await repo.GetActiveTimerAsync(request.UserId, cancellationToken) is not null)
            return null;

        var stopped = await db.TimeEntries
            .Where(t => t.UserId == request.UserId && !t.IsManual
                && t.TimerStart != null && t.TimerStop == request.StoppedAt)
            .OrderByDescending(t => t.TimerStart)
            .FirstOrDefaultAsync(cancellationToken);
        if (stopped is null)
            return null;

        var syncItems = await db.SyncQueueEntries
            .Where(q => q.EntityType == "TimeEntry" && q.EntityId == stopped.Id && q.Operation == "CreateTimeActivity")
            .ToListAsync(cancellationToken);

        TimeEntry resumed;
        if (syncItems.Any(q => q.Status is SyncStatus.Processing or SyncStatus.Completed))
        {
            resumed = new TimeEntry
            {
                UserId = request.UserId,
                JobId = stopped.JobId,
                Date = DateOnly.FromDateTime(request.StoppedAt.UtcDateTime),
                DurationMinutes = 0,
                Category = stopped.Category,
                TimerStart = request.StoppedAt,
                IsManual = false,
                OperationId = stopped.OperationId,
                WorkCenterId = stopped.WorkCenterId,
                EntryType = stopped.EntryType,
            };
            await repo.AddTimeEntryAsync(resumed, cancellationToken);
            db.LogActivityAt("timer-resumed",
                $"Resumed timer after an undone clock-out; entry {stopped.Id} was already sent to accounting",
                ("TimeEntry", resumed.Id));
        }
        else
        {
            db.SyncQueueEntries.RemoveRange(syncItems);
            stopped.TimerStop = null;
            stopped.DurationMinutes = 0;
            resumed = stopped;
            db.LogActivityAt("timer-resumed", "Resumed timer after an undone clock-out", ("TimeEntry", stopped.Id));
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = (await repo.GetTimeEntryByIdAsync(resumed.Id, cancellationToken))!;

        await timerHub.Clients.Group($"user:{request.UserId}")
            .SendAsync("timerStarted", new TimerStartedEvent(request.UserId, result), cancellationToken);

        return result;
    }
}
