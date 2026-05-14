using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetPlannedOrdersQuery(int? MrpRunId, MrpPlannedOrderStatus? Status) : IRequest<List<MrpPlannedOrderResponseModel>>;

public class GetPlannedOrdersHandler(AppDbContext db)
    : IRequestHandler<GetPlannedOrdersQuery, List<MrpPlannedOrderResponseModel>>
{
    public async Task<List<MrpPlannedOrderResponseModel>> Handle(GetPlannedOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.MrpPlannedOrders
            .AsNoTracking()
            .Include(po => po.Part)
            .AsQueryable();

        if (request.MrpRunId.HasValue)
            query = query.Where(po => po.MrpRunId == request.MrpRunId.Value);

        if (request.Status.HasValue)
            query = query.Where(po => po.Status == request.Status.Value);

        return await query
            .OrderBy(po => po.DueDate)
            .Select(po => new MrpPlannedOrderResponseModel(
                po.Id,
                po.MrpRunId,
                po.PartId,
                po.Part.PartNumber,
                po.Part.Description ?? po.Part.Name,
                po.OrderType,
                po.Status,
                po.Quantity,
                po.StartDate,
                po.DueDate,
                po.IsFirmed,
                po.ReleasedPurchaseOrderId,
                po.ReleasedJobId,
                po.ParentPlannedOrderId,
                po.Notes
            ))
            .ToListAsync(cancellationToken);
    }
}
