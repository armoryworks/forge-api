using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record GetSalesOrderInvoicesQuery(int SalesOrderId) : IRequest<List<SalesOrderInvoiceModel>>;

public class GetSalesOrderInvoicesHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrderInvoicesQuery, List<SalesOrderInvoiceModel>>
{
    public async Task<List<SalesOrderInvoiceModel>> Handle(
        GetSalesOrderInvoicesQuery request, CancellationToken cancellationToken)
    {
        // Verify sales order exists
        var exists = await db.SalesOrders.AnyAsync(so => so.Id == request.SalesOrderId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var invoices = await db.Invoices
            .Where(i => i.SalesOrderId == request.SalesOrderId)
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Include(i => i.Shipment)
            .AsNoTracking()
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync(cancellationToken);

        return invoices.Select(i =>
        {
            var paymentStatus = i.BalanceDue <= 0 ? "Paid" :
                i.AmountPaid > 0 ? "Partial" : "Unpaid";

            var shipmentNumbers = new List<string>();
            if (i.Shipment != null)
            {
                shipmentNumbers.Add(i.Shipment.ShipmentNumber);
            }

            return new SalesOrderInvoiceModel(
                i.Id,
                i.InvoiceNumber,
                i.Status.ToString(),
                i.Total,
                i.DueDate,
                paymentStatus,
                shipmentNumbers);
        }).ToList();
    }
}
