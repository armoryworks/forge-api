using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.RecurringOrders;

public record DeleteRecurringOrderCommand(int Id) : IRequest;

public class DeleteRecurringOrderHandler(IRecurringOrderRepository repo)
    : IRequestHandler<DeleteRecurringOrderCommand>
{
    public async Task Handle(DeleteRecurringOrderCommand request, CancellationToken cancellationToken)
    {
        var ro = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Recurring order {request.Id} not found");

        ro.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
