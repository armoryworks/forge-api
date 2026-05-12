using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — portal dashboard rollup. Counts open / pending rows so the
/// portal landing page can render KPI cards without each card making its
/// own list call.
///
/// Invoice count is conditionally returned: if the install has an
/// external accounting provider connected we don't surface invoice
/// numbers (the customer should see those in the provider's portal,
/// not ours — `⚡ Standalone Financial` boundary). Always-zero in
/// integrated mode.
/// </summary>
public record GetPortalDashboardQuery(int CustomerId) : IRequest<PortalSummaryResponseModel>;

public class GetPortalDashboardHandler(
    AppDbContext db,
    IAccountingService accounting)
    : IRequestHandler<GetPortalDashboardQuery, PortalSummaryResponseModel>
{
    public async Task<PortalSummaryResponseModel> Handle(GetPortalDashboardQuery request, CancellationToken ct)
    {
        var openSoStatuses = new[] { SalesOrderStatus.Confirmed, SalesOrderStatus.InProduction, SalesOrderStatus.PartiallyShipped };
        var openQuoteStatuses = new[] { QuoteStatus.Sent };
        var openInvoiceStatuses = new[] { InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };
        var inTransitShipmentStatuses = new[] { ShipmentStatus.Shipped, ShipmentStatus.InTransit };

        var openSoCount = await db.SalesOrders.AsNoTracking()
            .CountAsync(o => o.CustomerId == request.CustomerId && openSoStatuses.Contains(o.Status), ct);

        var openQuoteCount = await db.Quotes.AsNoTracking()
            .CountAsync(q => q.CustomerId == request.CustomerId && openQuoteStatuses.Contains(q.Status), ct);

        var standalone = string.Equals(accounting.ProviderId, "mock", StringComparison.OrdinalIgnoreCase);
        var openInvoiceCount = standalone
            ? await db.Invoices.AsNoTracking()
                .CountAsync(i => i.CustomerId == request.CustomerId && openInvoiceStatuses.Contains(i.Status), ct)
            : 0;

        var inTransitShipmentCount = await db.Shipments.AsNoTracking()
            .Include(s => s.SalesOrder)
            .CountAsync(s => s.SalesOrder.CustomerId == request.CustomerId
                && inTransitShipmentStatuses.Contains(s.Status), ct);

        return new PortalSummaryResponseModel(
            openSoCount, openQuoteCount, openInvoiceCount, inTransitShipmentCount);
    }
}
