using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record RecordClockPunchCommand(string EventType) : IRequest<ClockPunchResponseModel>;

public class RecordClockPunchValidator : AbstractValidator<RecordClockPunchCommand>
{
    public RecordClockPunchValidator()
    {
        RuleFor(x => x.EventType).Must(v => Enum.TryParse<ClockEventType>(v, true, out _))
            .WithMessage("EventType must be a ClockEventType.");
    }
}

/// <summary>
/// One tap on the Clock screen: the event goes through the same handler the
/// kiosk uses, attributed to the caller (the identified person on a shared
/// device). Returns the event id so undo can remove it within its window.
/// </summary>
public class RecordClockPunchHandler(AppDbContext db, IMediator mediator, IHttpContextAccessor httpContext)
    : IRequestHandler<RecordClockPunchCommand, ClockPunchResponseModel>
{
    public async Task<ClockPunchResponseModel> Handle(RecordClockPunchCommand request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var eventType = Enum.Parse<ClockEventType>(request.EventType, true);

        await mediator.Send(new ClockInOutCommand(userId, eventType.ToString()), ct);

        var eventId = await db.ClockEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => e.Id)
            .FirstAsync(ct);

        var state = await mediator.Send(new GetClockStateQuery(userId), ct);
        return new ClockPunchResponseModel(eventId, state);
    }
}
