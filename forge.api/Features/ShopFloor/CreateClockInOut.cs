using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record ClockInOutCommand(int UserId, string EventType) : IRequest;

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

public class ClockInOutHandler(AppDbContext db)
    : IRequestHandler<ClockInOutCommand>
{
    public async Task Handle(ClockInOutCommand request, CancellationToken ct)
    {
        _ = await db.Users.FindAsync([request.UserId], ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        // Keep legacy enum for backward compat during migration
        var eventType = Enum.TryParse<ClockEventType>(request.EventType, out var parsed)
            ? parsed : ClockEventType.ClockIn;

        db.ClockEvents.Add(new ClockEvent
        {
            UserId = request.UserId,
            EventType = eventType,
            EventTypeCode = request.EventType,
            Timestamp = DateTimeOffset.UtcNow,
            Source = "kiosk",
        });

        await db.SaveChangesAsync(ct);
    }
}
