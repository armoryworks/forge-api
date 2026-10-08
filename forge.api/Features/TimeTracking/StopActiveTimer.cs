using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.TimeTracking;

public record StopActiveTimerCommand(
    int UserId,
    DateTimeOffset StopAt,
    string? Notes = null,
    string? Reason = null,
    int? TimeEntryId = null,
    bool IncludeOperationTimers = false)
    : IRequest<StoppedTimerResponseModel?>;

public class StopActiveTimerHandler(
    ITimeTrackingRepository repo,
    ITimerStopService timerStop) : IRequestHandler<StopActiveTimerCommand, StoppedTimerResponseModel?>
{
    public async Task<StoppedTimerResponseModel?> Handle(StopActiveTimerCommand request, CancellationToken cancellationToken)
    {
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (targets.Count == 0)
            return null;

        foreach (var entry in targets)
            timerStop.Close(entry, request.StopAt, request.Notes, request.Reason);

        await repo.SaveChangesAsync(cancellationToken);

        var results = await timerStop.PublishStoppedAsync(targets, cancellationToken);
        var primary = results[0];
        return new StoppedTimerResponseModel(primary.Id, primary.JobId, primary.JobNumber);
    }

    private async Task<List<TimeEntry>> ResolveTargetsAsync(StopActiveTimerCommand request, CancellationToken cancellationToken)
    {
        if (request.TimeEntryId is int timeEntryId)
        {
            var entry = await repo.FindTimeEntryAsync(timeEntryId, cancellationToken);
            return entry is { TimerStart: not null, TimerStop: null } && entry.UserId == request.UserId
                ? [entry]
                : [];
        }

        if (!request.IncludeOperationTimers)
        {
            var active = await repo.GetActiveTimerAsync(request.UserId, cancellationToken);
            return active is null ? [] : [active];
        }

        var open = await repo.GetOpenTimersAsync(request.UserId, cancellationToken);
        return open
            .OrderBy(t => t.JobOperationId.HasValue)
            .ThenByDescending(t => t.TimerStart)
            .ToList();
    }
}
