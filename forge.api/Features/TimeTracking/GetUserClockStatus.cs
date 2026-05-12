using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record UserClockStatusResponseModel(
    bool IsClockedIn,
    string Status,
    DateTimeOffset? ClockedInAt);

public record GetUserClockStatusQuery(int UserId) : IRequest<UserClockStatusResponseModel>;

public class GetUserClockStatusHandler(AppDbContext db, IClockEventTypeService clockEventTypeService)
    : IRequestHandler<GetUserClockStatusQuery, UserClockStatusResponseModel>
{
    public async Task<UserClockStatusResponseModel> Handle(GetUserClockStatusQuery request, CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;

        var latestEvent = await db.ClockEvents
            .Where(e => e.UserId == request.UserId && e.Timestamp >= today)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (latestEvent is null)
            return new UserClockStatusResponseModel(false, "Out", null);

        var eventTypeDefs = await clockEventTypeService.GetAllAsync(ct);
        var typeDef = eventTypeDefs.FirstOrDefault(d => d.Code == latestEvent.EventTypeCode);

        var status = typeDef?.StatusMapping ?? "Out";
        var countsAsActive = typeDef?.CountsAsActive ?? false;
        var clockedInAt = countsAsActive ? latestEvent.Timestamp : (DateTimeOffset?)null;

        return new UserClockStatusResponseModel(countsAsActive, status, clockedInAt);
    }
}
