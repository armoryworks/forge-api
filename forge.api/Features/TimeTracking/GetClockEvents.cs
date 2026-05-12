using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetClockEventsQuery(int? UserId, DateOnly? From, DateOnly? To) : IRequest<List<ClockEventResponseModel>>;

public class GetClockEventsHandler(ITimeTrackingRepository repo) : IRequestHandler<GetClockEventsQuery, List<ClockEventResponseModel>>
{
    public Task<List<ClockEventResponseModel>> Handle(GetClockEventsQuery request, CancellationToken cancellationToken)
        => repo.GetClockEventsAsync(request.UserId, request.From, request.To, cancellationToken);
}
