using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

public record ConfirmSalesOrderCommand(int Id) : IRequest<ConfirmSalesOrderResponseModel>;

public class ConfirmSalesOrderHandler(
    ISalesOrderRepository repo,
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    ISalesOrderAcceptanceGate acceptanceGate,
    IClock clock)
    : IRequestHandler<ConfirmSalesOrderCommand, ConfirmSalesOrderResponseModel>
{
    public async Task<ConfirmSalesOrderResponseModel> Handle(ConfirmSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        if (order.Status != SalesOrderStatus.Draft)
            throw new InvalidOperationException("Only Draft orders can be confirmed");

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == order.CustomerId, cancellationToken);
        if (customer is { IsOnCreditHold: true })
        {
            var reason = string.IsNullOrWhiteSpace(customer.CreditHoldReason) ? "" : $": {customer.CreditHoldReason.Trim()}";
            throw new InvalidOperationException(
                $"{customer.GetDisplayName()} is on credit hold{reason}. Release the hold before confirming this order.");
        }

        if (await ProductionTrackResolver.ResolveAsync(db, cancellationToken) is null)
            throw new InvalidOperationException(
                "No production track is set up, so no work orders can be created. Set one up in Admin before confirming.");

        // Confirming an SO auto-creates its work orders — block it until the customer's acceptance
        // proof is on file (no-op when CAP-O2C-SO-ACCEPTANCE is off).
        await acceptanceGate.EnsureReleasableAsync(order.Id, cancellationToken);

        order.Status = SalesOrderStatus.Confirmed;
        order.ConfirmedDate = clock.UtcNow;
        db.LogActivityAt("confirmed", $"Sales Order {order.OrderNumber} confirmed.", ("SalesOrder", order.Id));

        await repo.SaveChangesAsync(cancellationToken);

        var jobsBefore = await CountLinkedJobsAsync(order.Id, cancellationToken);

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await mediator.Publish(new SalesOrderConfirmedEvent(request.Id, userId), cancellationToken);

        var jobsAfter = await CountLinkedJobsAsync(order.Id, cancellationToken);
        return new ConfirmSalesOrderResponseModel(jobsAfter - jobsBefore);
    }

    private Task<int> CountLinkedJobsAsync(int salesOrderId, CancellationToken ct) =>
        db.Jobs.CountAsync(j => j.SalesOrderLine != null && j.SalesOrderLine.SalesOrderId == salesOrderId, ct);
}
