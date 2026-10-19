using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record SubmitEcoForReviewCommand(int Id) : IRequest;

public class SubmitEcoForReviewHandler(AppDbContext db) : IRequestHandler<SubmitEcoForReviewCommand>
{
    public async Task Handle(SubmitEcoForReviewCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        if (eco.Status != EcoStatus.Draft)
            throw new InvalidOperationException("Only a Draft ECO can be submitted for review");

        eco.Status = EcoStatus.Review;
        await db.SaveChangesAsync(cancellationToken);
    }
}
