using System.Security.Claims;

using FluentValidation;
using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Mobile;

public record RecordClockPunchCommand(string EventType) : IRequest<ClockPunchResponseModel>;

public class RecordClockPunchValidator : AbstractValidator<RecordClockPunchCommand>
{
    public RecordClockPunchValidator(IClockEventTypeService clockEventTypeService)
    {
        RuleFor(x => x.EventType)
            .NotEmpty()
            .MustAsync(async (code, ct) => await clockEventTypeService.GetByCodeAsync(code, ct) is not null)
            .WithMessage("EventType must be a valid clock event type code.");
    }
}

/// <summary>
/// One tap on the Clock screen: the event goes through the same handler the
/// kiosk uses, attributed to the caller (the identified person on a shared
/// device). Returns the event id so undo can remove it within its window.
/// </summary>
public class RecordClockPunchHandler(IMediator mediator, IHttpContextAccessor httpContext)
    : IRequestHandler<RecordClockPunchCommand, ClockPunchResponseModel>
{
    public async Task<ClockPunchResponseModel> Handle(RecordClockPunchCommand request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var punch = await mediator.Send(new ClockInOutCommand(userId, request.EventType), ct);

        var state = await mediator.Send(new GetClockStateQuery(userId), ct);
        return new ClockPunchResponseModel(punch.ClockEventId, state, punch.StoppedJobNumber);
    }
}
