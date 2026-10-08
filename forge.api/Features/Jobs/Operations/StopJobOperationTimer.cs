using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Operations;

public record StopJobOperationTimerCommand(int JobId, int OperationId, StopJobOperationTimerRequestModel Data)
    : IRequest<JobOperationTimerStopResponseModel>;

public class StopJobOperationTimerHandler(
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IMediator mediator,
    IClock clock) : IRequestHandler<StopJobOperationTimerCommand, JobOperationTimerStopResponseModel>
{
    public async Task<JobOperationTimerStopResponseModel> Handle(StopJobOperationTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!await db.Jobs.AnyAsync(j => j.Id == request.JobId, cancellationToken))
            throw new KeyNotFoundException($"Job {request.JobId} not found");

        var open = await db.TimeEntries
            .Where(t => t.UserId == userId && t.JobId == request.JobId && t.OperationId == request.OperationId
                && t.JobOperationId != null && t.TimerStart != null && t.TimerStop == null)
            .OrderByDescending(t => t.TimerStart)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is not int timeEntryId)
            return new JobOperationTimerStopResponseModel(false, null);

        var stopped = await mediator.Send(
            new StopActiveTimerCommand(userId, clock.UtcNow, request.Data.Notes, TimeEntryId: timeEntryId),
            cancellationToken);
        if (stopped is null)
            return new JobOperationTimerStopResponseModel(false, null);

        return new JobOperationTimerStopResponseModel(
            true, await repo.GetTimeEntryByIdAsync(stopped.TimeEntryId, cancellationToken));
    }
}
