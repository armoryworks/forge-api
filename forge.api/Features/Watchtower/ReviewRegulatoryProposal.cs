using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Watchtower;

/// <summary>
/// regulatory-watchtower / A-5 propose-and-confirm: an admin applies (Applied) or dismisses
/// (Dismissed) a proposal. Records the reviewer. Applying never mutates the calendar
/// automatically — the admin drives any resulting compliance-calendar change (see follow-up).
/// </summary>
public record ReviewRegulatoryProposalCommand(int Id, bool Apply) : IRequest;

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

        await db.SaveChangesAsync(cancellationToken);
    }
}
