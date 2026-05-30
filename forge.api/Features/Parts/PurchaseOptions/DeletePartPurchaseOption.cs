using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts.PurchaseOptions;

// UoM purchase-options effort — retire (soft-delete) a part's purchase option. Existing PO lines /
// price tiers that reference it keep their FK; the option just drops out of the active list.
public record DeletePartPurchaseOptionCommand(int PartId, int Id) : IRequest;

public class DeletePartPurchaseOptionHandler(AppDbContext db) : IRequestHandler<DeletePartPurchaseOptionCommand>
{
    public async Task Handle(DeletePartPurchaseOptionCommand request, CancellationToken ct)
    {
        var option = await db.PartPurchaseOptions
            .FirstOrDefaultAsync(o => o.Id == request.Id && o.PartId == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Purchase option {request.Id} not found for part {request.PartId}");

        option.DeletedAt = DateTimeOffset.UtcNow;

        db.LogActivityAt(
            "purchase-option-removed",
            $"Removed purchase option: {option.Label}",
            ("Part", request.PartId));

        await db.SaveChangesAsync(ct);
    }
}
