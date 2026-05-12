using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.SalesOrders;

public record DeleteSalesOrderCommand(int Id) : IRequest;

public class DeleteSalesOrderHandler(ISalesOrderRepository repo)
    : IRequestHandler<DeleteSalesOrderCommand>
{
    public async Task Handle(DeleteSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only Draft sales orders can be deleted");

        order.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
