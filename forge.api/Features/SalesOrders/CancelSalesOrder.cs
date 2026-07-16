using MediatR;

using Forge.Api.Features.Invoices;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders;

public record CancelSalesOrderCommand(int Id, decimal? FeeAmount = null, string? FeeReason = null) : IRequest;

public class CancelSalesOrderHandler(ISalesOrderRepository repo, IMediator mediator, IClock clock)
    : IRequestHandler<CancelSalesOrderCommand>
{
    public async Task Handle(CancelSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        // F-033: source-state whitelist — only open/in-flight orders may be cancelled;
        // re-cancel of Cancelled is a silent duplicate; InProduction/Shipped/Completed are committed
        if (order.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyShipped))
            throw new InvalidOperationException(
                $"Cannot cancel a sales order in status {order.Status}. Allowed: Draft, Confirmed, PartiallyShipped.");

        order.Status = SalesOrderStatus.Cancelled;

        var fee = request.FeeAmount;
        var chargeFee = fee is > 0m;
        if (chargeFee)
        {
            order.CancellationFeeAmount = fee;
            order.CancellationFeeReason = request.FeeReason;
        }

        await repo.SaveChangesAsync(cancellationToken);

        // Late-cancellation fee: billed as a one-line standalone invoice against the order —
        // no shipment, no completed lines. Reuses the canonical invoice-creation path.
        if (chargeFee)
        {
            var now = clock.UtcNow;
            var reasonSuffix = string.IsNullOrWhiteSpace(request.FeeReason) ? string.Empty : $" ({request.FeeReason})";
            await mediator.Send(new CreateInvoiceCommand(
                CustomerId: order.CustomerId,
                SalesOrderId: order.Id,
                ShipmentId: null,
                InvoiceDate: now,
                DueDate: now.AddDays(15),
                CreditTerms: "Net15",
                TaxRate: 0m,
                Notes: $"Cancellation fee for order {order.OrderNumber}",
                Lines:
                [
                    new CreateInvoiceLineModel(
                        PartId: null,
                        Description: $"Order cancellation fee — {order.OrderNumber}{reasonSuffix}",
                        Quantity: 1m,
                        UnitPrice: fee!.Value),
                ]),
                cancellationToken);
        }
    }
}
