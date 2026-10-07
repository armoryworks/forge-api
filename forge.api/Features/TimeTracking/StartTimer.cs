using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record StartTimerCommand(StartTimerRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class StartTimerHandler(
    ITimeTrackingRepository repo,
    IJobRepository jobRepository,
    IHttpContextAccessor httpContext,
    IHubContext<TimerHub> timerHub,
    IMediator mediator,
    IClock clock) : IRequestHandler<StartTimerCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(StartTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (request.Data.JobId is int jobId)
        {
            var job = await jobRepository.FindAsync(jobId, cancellationToken)
                ?? throw new KeyNotFoundException($"Job {jobId} not found");
            if (job.IsArchived || job.Disposition.HasValue)
                throw new InvalidOperationException("This work order is closed and can't take time.");
        }

        var now = clock.UtcNow;

        var active = await repo.GetActiveTimerAsync(userId, cancellationToken);
        if (active is not null)
        {
            if (!request.Data.SwitchFromActive)
                throw new InvalidOperationException(await AlreadyRunningMessageAsync(active, cancellationToken));

            await mediator.Send(new StopActiveTimerCommand(userId, now), cancellationToken);
        }

        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = request.Data.JobId,
            Date = DateOnly.FromDateTime(now.UtcDateTime),
            DurationMinutes = 0,
            Category = request.Data.Category?.Trim(),
            Notes = request.Data.Notes?.Trim(),
            TimerStart = now,
            IsManual = false,
            OperationId = request.Data.OperationId,
            EntryType = request.Data.EntryType,
        };

        await repo.AddTimeEntryAsync(entry, cancellationToken);

        var result = (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;

        await timerHub.Clients.Group($"user:{userId}")
            .SendAsync("timerStarted", new TimerStartedEvent(userId, result), cancellationToken);

        return result;
    }

    private async Task<string> AlreadyRunningMessageAsync(TimeEntry active, CancellationToken cancellationToken)
    {
        var jobNumber = active.Job?.JobNumber;
        if (jobNumber is null && active.JobId is int activeJobId)
            jobNumber = (await jobRepository.FindAsync(activeJobId, cancellationToken))?.JobNumber;

        return jobNumber is null
            ? "You're already running a timer. Stop it first."
            : $"You're already running a timer on {jobNumber}. Stop it first.";
    }
}
