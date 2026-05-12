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
    IHttpContextAccessor httpContext,
    IHubContext<TimerHub> timerHub) : IRequestHandler<StartTimerCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(StartTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Check for existing active timer
        var active = await repo.GetActiveTimerAsync(userId, cancellationToken);
        if (active is not null)
            throw new InvalidOperationException("A timer is already running. Stop it before starting a new one.");

        var now = DateTimeOffset.UtcNow;
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

        // Broadcast to all tabs for this user
        await timerHub.Clients.Group($"user:{userId}")
            .SendAsync("timerStarted", new TimerStartedEvent(userId, result), cancellationToken);

        return result;
    }
}
