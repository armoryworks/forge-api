using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.SalesOrders;

public record ConfirmSalesOrderCommand(int Id) : IRequest;

public class ConfirmSalesOrderHandler(
    ISalesOrderRepository repo, IMediator mediator, IHttpContextAccessor httpContext, ISalesOrderAcceptanceGate acceptanceGate)
    : IRequestHandler<ConfirmSalesOrderCommand>
{
    public async Task Handle(ConfirmSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only Draft orders can be confirmed");

        // Confirming an SO auto-creates its work orders — block it until the customer's acceptance
        // proof is on file (no-op when CAP-O2C-SO-ACCEPTANCE is off).
        await acceptanceGate.EnsureReleasableAsync(order.Id, cancellationToken);

        order.Status = SalesOrderStatus.Confirmed;
        order.ConfirmedDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await mediator.Publish(new SalesOrderConfirmedEvent(request.Id, userId), cancellationToken);
    }
}
