using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

public record CancelPurchaseOrderCommand(int Id) : IRequest;

public class CancelPurchaseOrderHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<CancelPurchaseOrderCommand>
{
    public async Task Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status == PurchaseOrderStatus.Received || po.Status == PurchaseOrderStatus.Closed)
            throw new InvalidOperationException("Cannot cancel a received or closed purchase order");

        po.Status = PurchaseOrderStatus.Cancelled;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
