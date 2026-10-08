using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetActiveTimersQuery(int UserId) : IRequest<List<TimeEntryResponseModel>>;

public class GetActiveTimersHandler(ITimeTrackingRepository repo)
    : IRequestHandler<GetActiveTimersQuery, List<TimeEntryResponseModel>>
{
    public Task<List<TimeEntryResponseModel>> Handle(GetActiveTimersQuery request, CancellationToken ct)
        => repo.GetOpenTimerResponsesAsync(request.UserId, ct);
}
