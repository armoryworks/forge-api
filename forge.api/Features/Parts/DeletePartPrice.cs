using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Hard-deletes a PartPrice history row. <see cref="Core.Entities.PartPrice"/>
/// extends BaseEntity (not BaseAuditableEntity) and intentionally has no
/// soft-delete column — for pre-beta, removed history rows are just gone.
/// Future: if PartPrice is promoted to BaseAuditableEntity for full audit
/// trails, switch this to a soft delete.
/// </summary>
public record DeletePartPriceCommand(int PartId, int PriceId) : IRequest;

public class DeletePartPriceHandler(AppDbContext db)
    : IRequestHandler<DeletePartPriceCommand>
{
    public async Task Handle(DeletePartPriceCommand request, CancellationToken ct)
    {
        var price = await db.PartPrices
            .FirstOrDefaultAsync(p => p.Id == request.PriceId && p.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException(
                $"PartPrice {request.PriceId} not found on Part {request.PartId}");

        db.PartPrices.Remove(price);
        await db.SaveChangesAsync(ct);
    }
}
