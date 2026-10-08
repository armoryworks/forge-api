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
        var stoppedEntries = await db.TimeEntries
            .Where(t => t.UserId == request.UserId && !t.IsManual
                && t.TimerStart != null && t.TimerStop == request.StoppedAt)
            .OrderBy(t => t.JobOperationId.HasValue)
            .ThenByDescending(t => t.TimerStart)
            .ToListAsync(cancellationToken);
        if (stoppedEntries.Count == 0)
            return null;

        var openGeneral = await repo.GetActiveTimerAsync(request.UserId, cancellationToken) is not null;
        var openOperationIds = (await repo.GetOpenTimersAsync(request.UserId, cancellationToken))
            .Where(t => t.JobOperationId.HasValue)
            .Select(t => t.JobOperationId!.Value)
            .ToHashSet();

        var resumedEntries = new List<TimeEntry>();
        foreach (var stopped in stoppedEntries)
        {
            if (stopped.JobOperationId is int jobOperationId)
            {
                if (!openOperationIds.Add(jobOperationId))
                    continue;
            }
            else
            {
                if (openGeneral)
                    continue;
                openGeneral = true;
            }

            resumedEntries.Add(await ResumeAsync(stopped, request, cancellationToken));
        }

        if (resumedEntries.Count == 0)
            return null;

        await db.SaveChangesAsync(cancellationToken);

        TimeEntryResponseModel? first = null;
        foreach (var resumed in resumedEntries)
        {
            var result = (await repo.GetTimeEntryByIdAsync(resumed.Id, cancellationToken))!;
            first ??= result;

            await timerHub.Clients.Group($"user:{request.UserId}")
                .SendAsync("timerStarted", new TimerStartedEvent(request.UserId, result), cancellationToken);
        }

        return first;
    }

    private async Task<TimeEntry> ResumeAsync(TimeEntry stopped, ResumeStoppedTimerCommand request, CancellationToken cancellationToken)
    {
        var syncItems = await db.SyncQueueEntries
            .Where(q => q.EntityType == "TimeEntry" && q.EntityId == stopped.Id && q.Operation == "CreateTimeActivity")
            .ToListAsync(cancellationToken);

        if (syncItems.Any(q => q.Status is SyncStatus.Processing or SyncStatus.Completed))
        {
            var resumed = new TimeEntry
            {
                UserId = request.UserId,
                JobId = stopped.JobId,
                Date = DateOnly.FromDateTime(request.StoppedAt.UtcDateTime),
                DurationMinutes = 0,
                Category = stopped.Category,
                TimerStart = request.StoppedAt,
                IsManual = false,
                OperationId = stopped.OperationId,
                JobOperationId = stopped.JobOperationId,
                WorkCenterId = stopped.WorkCenterId,
                EntryType = stopped.EntryType,
            };
            await repo.AddTimeEntryAsync(resumed, cancellationToken);
            db.LogActivityAt("timer-resumed",
                $"Resumed timer after an undone clock-out; entry {stopped.Id} was already sent to accounting",
                ("TimeEntry", resumed.Id));
            return resumed;
        }

        db.SyncQueueEntries.RemoveRange(syncItems);
        stopped.TimerStop = null;
        stopped.DurationMinutes = 0;
        db.LogActivityAt("timer-resumed", "Resumed timer after an undone clock-out", ("TimeEntry", stopped.Id));
        return stopped;
    }
}
