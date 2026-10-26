using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

public record GetPortalSalesOrdersQuery(int CustomerId) : IRequest<PortalList<PortalSalesOrderListItem>>;

public class GetPortalSalesOrdersHandler(AppDbContext db)
    : IRequestHandler<GetPortalSalesOrdersQuery, PortalList<PortalSalesOrderListItem>>
{
    public async Task<PortalList<PortalSalesOrderListItem>> Handle(GetPortalSalesOrdersQuery request, CancellationToken ct)
    {
        // Project after pulling lines so the computed Total getter resolves
        // server-side won't translate to SQL — sum lines in memory.
        var scope = db.SalesOrders.AsNoTracking()
            .Where(o => o.CustomerId == request.CustomerId && o.Status != SalesOrderStatus.Draft);
        var orders = await scope
            .Include(o => o.Lines)
            .Include(o => o.Currency)
            .OrderByDescending(o => o.CreatedAt)
            .Take(PortalReadRules.ListCap)
            .ToListAsync(ct);
        var totalCount = await PortalReadRules.TotalCountAsync(scope, orders.Count, ct);
        var currencyCode = await PortalReadRules.CustomerCurrencyCodeAsync(db, request.CustomerId, ct);

        return new(orders.Select(o => new PortalSalesOrderListItem(
            Id: o.Id,
            OrderNumber: o.OrderNumber,
            Status: o.Status.ToString(),
            OrderDate: o.ConfirmedDate ?? o.CreatedAt,
            RequestedDate: o.RequestedDeliveryDate,
            Total: o.Total,
            CurrencyCode: o.Currency?.Code ?? currencyCode)).ToList(), totalCount);
    }
}
