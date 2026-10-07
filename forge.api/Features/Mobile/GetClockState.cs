using System.Security.Claims;

using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetClockStateQuery(int? UserId = null) : IRequest<ClockStateResponseModel>;

public class GetClockStateHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IClockEventTypeService clockEventTypeService)
    : IRequestHandler<GetClockStateQuery, ClockStateResponseModel>
{
    public async Task<ClockStateResponseModel> Handle(GetClockStateQuery request, CancellationToken ct)
    {
        var userId = request.UserId
            ?? int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var last = await ClockStateRules.LatestEventAsync(db, userId, ct);

        var (status, countsAsActive) = await ClockStateRules.ResolveStatusAsync(last, clockEventTypeService, ct);
        var state = ClockStateRules.ToPhoneState(status, countsAsActive);

        return new ClockStateResponseModel(state, last?.EventType.ToString(), last?.Timestamp, last?.Id);
    }
}
