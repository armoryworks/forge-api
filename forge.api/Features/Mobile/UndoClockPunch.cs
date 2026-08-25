using System.Security.Claims;
using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record UndoClockPunchCommand(int EventId) : IRequest<ClockStateResponseModel>;

/// <summary>
/// The compensating action for a clock punch: removes the caller's own
/// latest event, only inside the undo window. Anything older goes through
/// the time-correction flow on the desktop instead. Audited.
/// </summary>
public class UndoClockPunchHandler(
    AppDbContext db,
    IMediator mediator,
    IClock clock,
    IHttpContextAccessor httpContext,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<UndoClockPunchCommand, ClockStateResponseModel>
{
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(45);

    public async Task<ClockStateResponseModel> Handle(UndoClockPunchCommand request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var latest = await db.ClockEvents
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (latest is null || latest.Id != request.EventId)
            throw new InvalidOperationException("Only the most recent clock event can be undone.");
        if (clock.UtcNow - latest.Timestamp > UndoWindow)
            throw new InvalidOperationException("The undo window for this clock event has passed.");

        db.ClockEvents.Remove(latest);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(
            "ClockEventUndone", userId,
            entityType: "ClockEvent", entityId: latest.Id,
            details: JsonSerializer.Serialize(new { latest.EventType, latest.Timestamp }),
            ct: ct);

        return await mediator.Send(new GetClockStateQuery(userId), ct);
    }
}
