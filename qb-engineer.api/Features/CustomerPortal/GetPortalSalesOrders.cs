using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.CustomerPortal;

public record GetPortalSalesOrdersQuery(int CustomerId) : IRequest<List<PortalSalesOrderListItem>>;

public class GetPortalSalesOrdersHandler(AppDbContext db)
    : IRequestHandler<GetPortalSalesOrdersQuery, List<PortalSalesOrderListItem>>
{
    public async Task<List<PortalSalesOrderListItem>> Handle(GetPortalSalesOrdersQuery request, CancellationToken ct)
    {
        // Project after pulling lines so the computed Total getter resolves
        // server-side won't translate to SQL — sum lines in memory.
        var orders = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.CustomerId == request.CustomerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return orders.Select(o => new PortalSalesOrderListItem(
            Id: o.Id,
            OrderNumber: o.OrderNumber,
            Status: o.Status.ToString(),
            OrderDate: o.ConfirmedDate ?? o.CreatedAt,
            RequestedDate: o.RequestedDeliveryDate,
            Total: o.Total)).ToList();
    }
}
