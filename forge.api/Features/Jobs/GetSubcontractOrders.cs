using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetSubcontractOrdersQuery(int JobId) : IRequest<List<SubcontractOrderResponseModel>>;

public class GetSubcontractOrdersHandler(AppDbContext db)
    : IRequestHandler<GetSubcontractOrdersQuery, List<SubcontractOrderResponseModel>>
{
    public async Task<List<SubcontractOrderResponseModel>> Handle(GetSubcontractOrdersQuery request, CancellationToken ct)
    {
        return await db.SubcontractOrders
            .AsNoTracking()
            .Where(o => o.JobId == request.JobId)
            .OrderByDescending(o => o.SentAt)
            .Select(o => new SubcontractOrderResponseModel(
                o.Id, o.JobId, o.Job.JobNumber ?? "",
                o.OperationId, o.Operation.Title,
                o.VendorId, o.Vendor.CompanyName,
                o.PurchaseOrderId, o.PurchaseOrder != null ? o.PurchaseOrder.PONumber : null,
                o.Quantity, o.UnitCost, o.Quantity * o.UnitCost,
                o.SentAt, o.ExpectedReturnDate, o.ReceivedAt,
                o.ReceivedQuantity, o.Status.ToString(),
                o.ShippingTrackingNumber, o.ReturnTrackingNumber, o.Notes,
                o.CreatedAt))
            .ToListAsync(ct);
    }
}
