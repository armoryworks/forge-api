using FluentValidation;
using MediatR;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.ShopFloor;

public record ClockInOutCommand(int UserId, string EventType, string Source = "kiosk") : IRequest<ClockInOutResponseModel>;

public class ClockInOutValidator : AbstractValidator<ClockInOutCommand>
{
    public ClockInOutValidator(IClockEventTypeService clockEventTypeService)
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.EventType)
            .NotEmpty()
            .MustAsync(async (code, ct) => await clockEventTypeService.GetByCodeAsync(code, ct) is not null)
            .WithMessage("EventType must be a valid clock event type code");
    }
}

public class ClockInOutHandler(
    AppDbContext db,
    IClockEventTypeService clockEventTypeService,
    IMediator mediator,
    IClock clock)
    : IRequestHandler<ClockInOutCommand, ClockInOutResponseModel>
{
    public async Task<ClockInOutResponseModel> Handle(ClockInOutCommand request, CancellationToken ct)
    {
        _ = await db.Users.FindAsync([request.UserId], ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        var definition = await clockEventTypeService.GetByCodeAsync(request.EventType, ct)
            ?? throw new KeyNotFoundException($"Clock event type {request.EventType} not found");

        var timestamp = clock.UtcNow;
        var clockEvent = new ClockEvent
        {
            UserId = request.UserId,
            EventType = LegacyClockEventType.From(definition),
            EventTypeCode = request.EventType,
            Timestamp = timestamp,
            Source = request.Source,
        };
        db.ClockEvents.Add(clockEvent);

        StoppedTimerResponseModel? stopped = null;
        if (definition.StatusMapping == "Out")
            stopped = await mediator.Send(
                new StopActiveTimerCommand(request.UserId, timestamp, Reason: "clocked out"), ct);

        await db.SaveChangesAsync(ct);

        db.LogActivityAt("clock-event-recorded",
            ClockEventActivity.Describe(definition, clockEvent.Source, stopped),
            ("ClockEvent", clockEvent.Id));
        await db.SaveChangesAsync(ct);

        return new ClockInOutResponseModel(clockEvent.Id, stopped?.JobNumber);
    }
}
