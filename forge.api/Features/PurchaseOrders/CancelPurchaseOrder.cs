using MediatR;
using Forge.Core.Enums;
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

        // F-033: source-state whitelist — only pre-receipt POs may be cancelled;
        // re-cancel of Cancelled is a silent duplicate; PartiallyReceived/Received/Closed have committed stock
        if (po.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Acknowledged))
            throw new InvalidOperationException(
                $"Cannot cancel a purchase order in status {po.Status}. Allowed: Draft, Submitted, Acknowledged.");

        po.Status = PurchaseOrderStatus.Cancelled;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
