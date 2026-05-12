using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record GetPurchaseOrderReleasesQuery(int PurchaseOrderId) : IRequest<List<PurchaseOrderReleaseResponseModel>>;

public class GetPurchaseOrderReleasesHandler(AppDbContext db) : IRequestHandler<GetPurchaseOrderReleasesQuery, List<PurchaseOrderReleaseResponseModel>>
{
    public async Task<List<PurchaseOrderReleaseResponseModel>> Handle(GetPurchaseOrderReleasesQuery request, CancellationToken cancellationToken)
    {
        var po = await db.PurchaseOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.PurchaseOrderId} not found");

        if (!po.IsBlanket)
            throw new InvalidOperationException("Releases are only available for blanket purchase orders");

        var releases = await db.PurchaseOrderReleases
            .AsNoTracking()
            .Include(r => r.PurchaseOrderLine)
                .ThenInclude(l => l.Part)
            .Where(r => r.PurchaseOrderId == request.PurchaseOrderId)
            .OrderBy(r => r.ReleaseNumber)
            .Select(r => new PurchaseOrderReleaseResponseModel
            {
                Id = r.Id,
                ReleaseNumber = r.ReleaseNumber,
                PurchaseOrderLineId = r.PurchaseOrderLineId,
                PartNumber = r.PurchaseOrderLine.Part.PartNumber,
                PartDescription = r.PurchaseOrderLine.Description,
                Quantity = r.Quantity,
                RequestedDeliveryDate = r.RequestedDeliveryDate,
                ActualDeliveryDate = r.ActualDeliveryDate,
                Status = r.Status,
                ReceivingRecordId = r.ReceivingRecordId,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return releases;
    }
}
