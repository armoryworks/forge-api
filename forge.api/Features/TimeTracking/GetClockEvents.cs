using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetClockEventsQuery(int? UserId, DateOnly? From, DateOnly? To) : IRequest<List<ClockEventResponseModel>>;

public class GetClockEventsHandler(ITimeTrackingRepository repo, IHttpContextAccessor http) : IRequestHandler<GetClockEventsQuery, List<ClockEventResponseModel>>
{
    public Task<List<ClockEventResponseModel>> Handle(GetClockEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = CallerAccess.ScopeToSelf(
            http.HttpContext?.User, request.UserId, "You can only view your own clock events.");
        return repo.GetClockEventsAsync(userId, request.From, request.To, cancellationToken);
    }
}
