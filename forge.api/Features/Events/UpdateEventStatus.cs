using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Events;

/// <summary>
/// compliance-calendar A-4: set the workflow status (+ owner/evidence) on a tracking-tier
/// event. Rejected for reminder-tier events (type/group not flagged RequiresTracking).
/// </summary>
public record UpdateEventStatusCommand(
    int Id, string Status, int? OwnerUserId, string? WaivedReason, string? EvidenceUrl, int? EvidenceDocumentSetId)
    : IRequest;

public class UpdateEventStatusValidator : AbstractValidator<UpdateEventStatusCommand>
{
    public UpdateEventStatusValidator()
    {
        RuleFor(x => x.Status).NotEmpty()
            .Must(s => Enum.TryParse<EventStatus>(s, true, out _)).WithMessage("Invalid status");
        RuleFor(x => x.WaivedReason).MaximumLength(1000);
        RuleFor(x => x.EvidenceUrl).MaximumLength(1000);
    }
}

public class UpdateEventStatusHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpdateEventStatusCommand>
{
    public async Task Handle(UpdateEventStatusCommand request, CancellationToken cancellationToken)
    {
        var evt = await db.Events
            .Include(e => e.CalendarEventType).ThenInclude(t => t!.SuperGroup)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Event {request.Id} not found");

        var requiresTracking = (evt.CalendarEventType?.RequiresTracking ?? false)
            || (evt.CalendarEventType?.SuperGroup?.RequiresTracking ?? false);
        if (!requiresTracking)
            throw new InvalidOperationException("This event's type is not a tracking (workflow) type.");

        var status = Enum.Parse<EventStatus>(request.Status, true);
        evt.Status = status;
        evt.OwnerUserId = request.OwnerUserId;
        evt.EvidenceUrl = request.EvidenceUrl;
        evt.EvidenceDocumentSetId = request.EvidenceDocumentSetId;

        if (status == EventStatus.Done)
        {
            evt.CompletedByUserId = db.CurrentUserId;
            evt.CompletedAt = clock.UtcNow;
        }
        else
        {
            evt.CompletedByUserId = null;
            evt.CompletedAt = null;
        }
        evt.WaivedReason = status == EventStatus.Waived ? request.WaivedReason : null;

        var detail = status == EventStatus.Waived && !string.IsNullOrWhiteSpace(request.WaivedReason)
            ? $"Status set to {status} — {request.WaivedReason}"
            : $"Status set to {status}";
        db.LogActivityAt("status-changed", detail, ("Event", evt.Id));

        await db.SaveChangesAsync(cancellationToken);
    }
}
