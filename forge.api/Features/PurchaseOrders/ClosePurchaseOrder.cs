using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

public record ClosePurchaseOrderCommand(int Id) : IRequest;

public class ClosePurchaseOrderHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<ClosePurchaseOrderCommand>
{
    public async Task Handle(ClosePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status != PurchaseOrderStatus.Received)
            throw new InvalidOperationException("Only received purchase orders can be closed");

        po.Status = PurchaseOrderStatus.Closed;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
