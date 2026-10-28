using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Tolerances;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetSubcontractOrdersQuery(int JobId) : IRequest<List<SubcontractOrderResponseModel>>;

public class GetSubcontractOrdersHandler(AppDbContext db)
    : IRequestHandler<GetSubcontractOrdersQuery, List<SubcontractOrderResponseModel>>
{
    public async Task<List<SubcontractOrderResponseModel>> Handle(GetSubcontractOrdersQuery request, CancellationToken ct)
    {
        var orders = await db.SubcontractOrders
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
                o.CreatedAt,
                o.ScrapQuantity,
                0m, 0m, 0m, 0m,
                o.OverToleranceReason))
            .ToListAsync(ct);

        var tolerances = await SubcontractTolerances.ForOrdersAsync(
            db, orders.Select(o => (o.Id, o.VendorId, o.PurchaseOrderId)).ToList(), ct);
        return orders.Select(o =>
        {
            var tolerance = tolerances[o.Id];
            return o with
            {
                OverTolerancePct = tolerance.OverPercent,
                UnderTolerancePct = tolerance.UnderPercent,
                MaxAcceptedQuantity = tolerance.MaxAccepted(o.Quantity),
                MinCompleteQuantity = tolerance.MinComplete(o.Quantity),
            };
        }).ToList();
    }
}
