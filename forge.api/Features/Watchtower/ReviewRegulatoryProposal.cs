using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Watchtower;

/// <summary>
/// regulatory-watchtower / A-5/A-8 propose-and-confirm: an admin applies (Applied) or dismisses
/// (Dismissed) a proposal, with the reviewer recorded. On apply, if a target Event-Type and a due
/// date are supplied, a system-generated compliance-calendar deadline is created — the confirm
/// step that turns a proposal into a calendar item. Nothing is ever auto-applied without the admin.
/// </summary>
public record ReviewRegulatoryProposalCommand(
    int Id, bool Apply, DateTimeOffset? DueDate = null, int? TargetEventTypeId = null) : IRequest;

public class ReviewRegulatoryProposalHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ReviewRegulatoryProposalCommand>
{
    public async Task Handle(ReviewRegulatoryProposalCommand request, CancellationToken cancellationToken)
    {
        var proposal = await db.RegulatoryChangeProposals
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Proposal {request.Id} not found");

        proposal.Status = request.Apply ? RegulatoryProposalStatus.Applied : RegulatoryProposalStatus.Dismissed;
        proposal.ReviewedByUserId = db.CurrentUserId;
        proposal.ReviewedAt = clock.UtcNow;

        if (request.Apply)
        {
            var typeId = request.TargetEventTypeId ?? proposal.TargetEventTypeId;
            if (typeId is int t)
                proposal.TargetEventTypeId = t;

            // A-8: apply → a system-generated compliance-calendar deadline (admin-driven).
            if (typeId is int eventTypeId && request.DueDate is DateTimeOffset due)
            {
                db.Events.Add(new Event
                {
                    Title = proposal.Title,
                    Description = proposal.Details,
                    StartTime = due,
                    EndTime = due,
                    EventType = EventType.Other,   // legacy enum (expand phase)
                    EventTypeId = eventTypeId,
                    IsAllDay = true,
                    IsSystemGenerated = true,
                    CreatedByUserId = db.CurrentUserId ?? 0,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
