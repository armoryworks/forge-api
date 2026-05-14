using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record DeletePlannedOrderCommand(int Id) : IRequest;

public class DeletePlannedOrderHandler(AppDbContext db)
    : IRequestHandler<DeletePlannedOrderCommand>
{
    public async Task Handle(DeletePlannedOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.MrpPlannedOrders
            .FirstOrDefaultAsync(po => po.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planned order {request.Id} not found.");

        if (order.Status == MrpPlannedOrderStatus.Released)
            throw new InvalidOperationException("Cannot delete a released planned order.");

        order.Status = MrpPlannedOrderStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
    }
}
