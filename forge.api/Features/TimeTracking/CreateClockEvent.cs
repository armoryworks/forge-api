using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.TimeTracking;

public record CreateClockEventCommand(CreateClockEventRequestModel Data) : IRequest<ClockEventResponseModel>;

public class CreateClockEventValidator : AbstractValidator<CreateClockEventCommand>
{
    public CreateClockEventValidator(IClockEventTypeService clockEventTypeService)
    {
        RuleFor(x => x.Data.EventTypeCode)
            .NotEmpty()
            .MustAsync(async (code, ct) => await clockEventTypeService.GetByCodeAsync(code, ct) is not null)
            .WithMessage("EventTypeCode must be a valid clock event type code");
        RuleFor(x => x.Data.Reason).MaximumLength(500).When(x => x.Data.Reason is not null);
        RuleFor(x => x.Data.ScanMethod).MaximumLength(50).When(x => x.Data.ScanMethod is not null);
        RuleFor(x => x.Data.Source).MaximumLength(50).When(x => x.Data.Source is not null);
    }
}

public class CreateClockEventHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IClockEventTypeService clockEventTypeService,
    IMediator mediator,
    IClock clock) : IRequestHandler<CreateClockEventCommand, ClockEventResponseModel>
{
    public async Task<ClockEventResponseModel> Handle(CreateClockEventCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var data = request.Data;

        var definition = await clockEventTypeService.GetByCodeAsync(data.EventTypeCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Clock event type {data.EventTypeCode} not found");

        var clockEvent = new ClockEvent
        {
            UserId = userId,
            EventType = LegacyClockEventType.From(definition),
            EventTypeCode = data.EventTypeCode,
            Reason = data.Reason?.Trim(),
            ScanMethod = data.ScanMethod?.Trim(),
            Timestamp = clock.UtcNow,
            Source = data.Source?.Trim(),
        };
        db.ClockEvents.Add(clockEvent);

        StoppedTimerResponseModel? stopped = null;
        if (definition.StatusMapping == "Out")
            stopped = await mediator.Send(
                new StopActiveTimerCommand(userId, clockEvent.Timestamp, Reason: "clocked out"), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("clock-event-recorded",
            ClockEventActivity.Describe(definition, clockEvent.Source, stopped),
            ("ClockEvent", clockEvent.Id));
        await db.SaveChangesAsync(cancellationToken);

        // Return populated response
        var user = httpContext.HttpContext!.User;
        var userName = user.FindFirstValue(ClaimTypes.GivenName) + " " + user.FindFirstValue(ClaimTypes.Surname);

        return new ClockEventResponseModel(
            clockEvent.Id, userId, userName.Trim(), clockEvent.EventTypeCode,
            clockEvent.Reason, clockEvent.ScanMethod, clockEvent.Timestamp, clockEvent.Source);
    }
}
