using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

public record DeleteContactInteractionCommand(int CustomerId, int InteractionId) : IRequest;

public class DeleteContactInteractionHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteContactInteractionCommand>
{
    public async Task Handle(DeleteContactInteractionCommand request, CancellationToken cancellationToken)
    {
        var interaction = await db.ContactInteractions
            .Include(ci => ci.Contact)
            .FirstOrDefaultAsync(ci => ci.Id == request.InteractionId
                && ci.Contact.CustomerId == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Interaction {request.InteractionId} not found for customer {request.CustomerId}");

        // Soft-delete (was hard-delete via Remove() — bug fix). ContactInteraction
        // extends BaseAuditableEntity and the global query filter hides it
        // from subsequent reads. DeletedBy auto-stamped by SetTimestamps.
        interaction.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "interaction-removed",
            $"Removed interaction: {interaction.Subject}",
            ("Customer", request.CustomerId),
            ("Contact", interaction.ContactId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
