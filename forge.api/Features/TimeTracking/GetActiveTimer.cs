using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetActiveTimerQuery(int UserId) : IRequest<ActiveTimerResponseModel?>;

public class GetActiveTimerHandler(ITimeTrackingRepository repo, IJobRepository jobs)
    : IRequestHandler<GetActiveTimerQuery, ActiveTimerResponseModel?>
{
    public async Task<ActiveTimerResponseModel?> Handle(GetActiveTimerQuery request, CancellationToken ct)
    {
        var active = await repo.GetActiveTimerAsync(request.UserId, ct);
        if (active?.TimerStart is null)
            return null;

        string? jobNumber = null;
        if (active.JobId.HasValue)
            jobNumber = (await jobs.FindAsync(active.JobId.Value, ct))?.JobNumber;

        return new ActiveTimerResponseModel(
            active.Id,
            active.JobId,
            jobNumber,
            active.OperationId,
            active.TimerStart.Value);
    }
}
