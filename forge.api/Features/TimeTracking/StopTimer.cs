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

        var stopped = await mediator.Send(
            new StopActiveTimerCommand(userId, clock.UtcNow, request.Data.Notes), cancellationToken)
            ?? throw new InvalidOperationException("No active timer found.");

        return (await repo.GetTimeEntryByIdAsync(stopped.TimeEntryId, cancellationToken))!;
    }
}
