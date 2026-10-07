using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record UserClockStatusResponseModel(
    bool IsClockedIn,
    string Status,
    DateTimeOffset? ClockedInAt);

public record GetUserClockStatusQuery(int UserId) : IRequest<UserClockStatusResponseModel>;

public class GetUserClockStatusHandler(AppDbContext db, IClockEventTypeService clockEventTypeService, IClock clock)
    : IRequestHandler<GetUserClockStatusQuery, UserClockStatusResponseModel>
{
    public async Task<UserClockStatusResponseModel> Handle(GetUserClockStatusQuery request, CancellationToken ct)
    {
        var latestEvents = await ClockStateRules.LatestEventsAsync(db, [request.UserId], clock.UtcNow, ct: ct);
        latestEvents.TryGetValue(request.UserId, out var latestEvent);

        var (status, countsAsActive) = await ClockStateRules.ResolveStatusAsync(latestEvent, clockEventTypeService, ct);
        var clockedInAt = countsAsActive ? latestEvent!.Timestamp : (DateTimeOffset?)null;

        return new UserClockStatusResponseModel(countsAsActive, status, clockedInAt);
    }
}
