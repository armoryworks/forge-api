using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PurchaseOrders;

public record DeletePurchaseOrderCommand(int Id) : IRequest;

public class DeletePurchaseOrderHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<DeletePurchaseOrderCommand>
{
    public async Task Handle(DeletePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only Draft purchase orders can be deleted");

        po.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
