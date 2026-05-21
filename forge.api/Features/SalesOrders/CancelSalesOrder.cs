using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.SalesOrders;

public record CancelSalesOrderCommand(int Id) : IRequest;

public class CancelSalesOrderHandler(ISalesOrderRepository repo)
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

        await repo.SaveChangesAsync(cancellationToken);
    }
}
