using System.Security.Claims;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetClockStateQuery(int? UserId = null) : IRequest<ClockStateResponseModel>;

public class GetClockStateHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<GetClockStateQuery, ClockStateResponseModel>
{
    public async Task<ClockStateResponseModel> Handle(GetClockStateQuery request, CancellationToken ct)
    {
        var userId = request.UserId
            ?? int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var last = await db.ClockEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new { e.Id, e.EventType, e.Timestamp })
            .FirstOrDefaultAsync(ct);

        var state = last?.EventType switch
        {
            ClockEventType.ClockIn or ClockEventType.BreakEnd or ClockEventType.LunchEnd => "in",
            ClockEventType.BreakStart or ClockEventType.LunchStart => "break",
            _ => "out",
        };

        return new ClockStateResponseModel(state, last?.EventType.ToString(), last?.Timestamp, last?.Id);
    }
}
