using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record StopTimerCommand(StopTimerRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class StopTimerHandler(
    ITimeTrackingRepository repo,
    IHttpContextAccessor httpContext,
    IMediator mediator,
    IClock clock) : IRequestHandler<StopTimerCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(StopTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (request.Data.TimeEntryId is int timeEntryId)
        {
            var entry = await repo.FindTimeEntryAsync(timeEntryId, cancellationToken);
            if (entry is null || entry.UserId != userId || entry.TimerStart is null)
                throw new KeyNotFoundException($"Timer {timeEntryId} not found.");
            if (entry.TimerStop is not null)
                return (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;

            return await StopAsync(userId, entry.Id, request.Data.Notes, cancellationToken);
        }

        var open = await repo.GetOpenTimersAsync(userId, cancellationToken);
        if (request.Data.JobId is int jobId)
            open = open.Where(t => t.JobId == jobId).ToList();

        var target = open.FirstOrDefault(t => t.JobOperationId is null)
            ?? (open.Count == 1 ? open[0] : null);
        if (target is null)
            throw new InvalidOperationException(open.Count == 0
                ? "No active timer found."
                : "Several timers are running — choose which one to stop.");

        return await StopAsync(userId, target.Id, request.Data.Notes, cancellationToken);
    }

    private async Task<TimeEntryResponseModel> StopAsync(int userId, int timeEntryId, string? notes, CancellationToken cancellationToken)
    {
        var stopped = await mediator.Send(
            new StopActiveTimerCommand(userId, clock.UtcNow, notes, TimeEntryId: timeEntryId), cancellationToken)
            ?? throw new InvalidOperationException("No active timer found.");

        return (await repo.GetTimeEntryByIdAsync(stopped.TimeEntryId, cancellationToken))!;
    }
}
