using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetPendingInspectionsQuery : IRequest<List<PendingInspectionItem>>;

public class GetPendingInspectionsHandler(AppDbContext db)
    : IRequestHandler<GetPendingInspectionsQuery, List<PendingInspectionItem>>
{
    public async Task<List<PendingInspectionItem>> Handle(GetPendingInspectionsQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return await db.ReceivingRecords
            .AsNoTracking()
            .Where(r => r.InspectionStatus == ReceivingInspectionStatus.Pending
                     || r.InspectionStatus == ReceivingInspectionStatus.InProgress)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new PendingInspectionItem(
                r.Id,
                r.PurchaseOrderLine.Part != null ? r.PurchaseOrderLine.Part.PartNumber : "",
                r.PurchaseOrderLine.Part != null ? r.PurchaseOrderLine.Part.Description ?? "" : "",
                r.PurchaseOrderLine.PurchaseOrder.PONumber,
                r.PurchaseOrderLine.PurchaseOrder.Vendor != null ? r.PurchaseOrderLine.PurchaseOrder.Vendor.CompanyName : "",
                r.QuantityReceived,
                r.CreatedAt,
                null,
                (int)(now - r.CreatedAt).TotalDays))
            .ToListAsync(ct);
    }
}
