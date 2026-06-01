using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseUnits;

// UoM purchase-units effort — retire (soft-delete) a part's purchase unit. Existing PO lines /
// price tiers that reference it keep their FK; the option just drops out of the active list.
public record DeletePartPurchaseUnitCommand(int PartId, int Id) : IRequest;

public class DeletePartPurchaseUnitHandler(AppDbContext db) : IRequestHandler<DeletePartPurchaseUnitCommand>
{
    public async Task Handle(DeletePartPurchaseUnitCommand request, CancellationToken ct)
    {
        var option = await db.PartPurchaseUnits
            .FirstOrDefaultAsync(o => o.Id == request.Id && o.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Purchase unit {request.Id} not found for part {request.PartId}");

        option.DeletedAt = DateTimeOffset.UtcNow;

        db.LogActivityAt(
            "purchase-unit-removed",
            $"Removed purchase unit: {option.Label}",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);
    }
}
