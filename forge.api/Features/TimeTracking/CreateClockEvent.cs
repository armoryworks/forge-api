using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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

public class CreateClockEventHandler(ITimeTrackingRepository repo, IHttpContextAccessor httpContext) : IRequestHandler<CreateClockEventCommand, ClockEventResponseModel>
{
    public async Task<ClockEventResponseModel> Handle(CreateClockEventCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var data = request.Data;

        // Keep legacy enum for backward compat during migration
        var eventType = Enum.TryParse<ClockEventType>(data.EventTypeCode, out var parsed)
            ? parsed : ClockEventType.ClockIn;

        var clockEvent = new ClockEvent
        {
            UserId = userId,
            EventType = eventType,
            EventTypeCode = data.EventTypeCode,
            Reason = data.Reason?.Trim(),
            ScanMethod = data.ScanMethod?.Trim(),
            Timestamp = DateTimeOffset.UtcNow,
            Source = data.Source?.Trim(),
        };

        await repo.AddClockEventAsync(clockEvent, cancellationToken);

        // Return populated response
        var user = httpContext.HttpContext!.User;
        var userName = user.FindFirstValue(ClaimTypes.GivenName) + " " + user.FindFirstValue(ClaimTypes.Surname);

        return new ClockEventResponseModel(
            clockEvent.Id, userId, userName.Trim(), clockEvent.EventTypeCode,
            clockEvent.Reason, clockEvent.ScanMethod, clockEvent.Timestamp, clockEvent.Source);
    }
}
