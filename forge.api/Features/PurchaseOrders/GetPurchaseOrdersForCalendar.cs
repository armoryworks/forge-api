using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record GetPurchaseOrdersForCalendarQuery(DateOnly From, DateOnly To) : IRequest<List<PoCalendarResponseModel>>;

public class GetPurchaseOrdersForCalendarHandler(AppDbContext db)
    : IRequestHandler<GetPurchaseOrdersForCalendarQuery, List<PoCalendarResponseModel>>
{
    public async Task<List<PoCalendarResponseModel>> Handle(
        GetPurchaseOrdersForCalendarQuery request, CancellationToken cancellationToken)
    {
        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return await db.PurchaseOrders
            .Include(po => po.Vendor)
            .Include(po => po.Lines)
            .Where(po => po.DeletedAt == null)
            .Where(po => po.ExpectedDeliveryDate.HasValue)
            .Where(po => po.ExpectedDeliveryDate!.Value >= fromUtc)
            .Where(po => po.ExpectedDeliveryDate!.Value <= toUtc)
            .Where(po => po.Status != PurchaseOrderStatus.Received
                      && po.Status != PurchaseOrderStatus.Closed
                      && po.Status != PurchaseOrderStatus.Cancelled)
            .Select(po => new PoCalendarResponseModel(
                po.Id,
                po.PONumber,
                po.Vendor != null ? po.Vendor.CompanyName : "Unknown Vendor",
                DateOnly.FromDateTime(po.ExpectedDeliveryDate!.Value.UtcDateTime),
                po.Status.ToString(),
                po.Lines.Count
            ))
            .ToListAsync(cancellationToken);
    }
}
