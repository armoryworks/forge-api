using System.Security.Claims;

using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record UpdateSalesOrderCommand(
    int Id,
    int? ShippingAddressId,
    int? BillingAddressId,
    string? CreditTerms,
    DateTimeOffset? RequestedDeliveryDate,
    string? CustomerPO,
    string? Notes,
    decimal? TaxRate) : IRequest;

public class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId.HasValue);
        RuleFor(x => x.BillingAddressId).GreaterThan(0).When(x => x.BillingAddressId.HasValue);
        RuleFor(x => x.CreditTerms).MaximumLength(50).When(x => x.CreditTerms is not null);
        RuleFor(x => x.CustomerPO).MaximumLength(100).When(x => x.CustomerPO is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).When(x => x.TaxRate.HasValue);
    }
}

public class UpdateSalesOrderHandler(
    ISalesOrderRepository repo,
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext)
    : IRequestHandler<UpdateSalesOrderCommand>
{
    public async Task Handle(UpdateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        if (order.Status != SalesOrderStatus.Draft && order.Status != SalesOrderStatus.Confirmed)
            throw new InvalidOperationException("Can only update Draft or Confirmed sales orders");

        var oldDeliveryDate = order.RequestedDeliveryDate;

        if (request.ShippingAddressId.HasValue) order.ShippingAddressId = request.ShippingAddressId;
        if (request.BillingAddressId.HasValue) order.BillingAddressId = request.BillingAddressId;
        if (request.CreditTerms != null) order.CreditTerms = Enum.Parse<CreditTerms>(request.CreditTerms, true);
        if (request.RequestedDeliveryDate.HasValue) order.RequestedDeliveryDate = request.RequestedDeliveryDate;
        if (request.CustomerPO != null) order.CustomerPO = request.CustomerPO;
        if (request.Notes != null) order.Notes = request.Notes;
        if (request.TaxRate.HasValue) order.TaxRate = request.TaxRate.Value;

        await repo.SaveChangesAsync(cancellationToken);

        // Publish DeliveryDateChangedEvent for each line when the delivery date changes
        if (request.RequestedDeliveryDate.HasValue && oldDeliveryDate.HasValue
            && request.RequestedDeliveryDate.Value != oldDeliveryDate.Value)
        {
            var userId = int.Parse(httpContext.HttpContext!.User
                .FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var lineIds = await db.SalesOrderLines
                .AsNoTracking()
                .Where(l => l.SalesOrderId == order.Id)
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            foreach (var lineId in lineIds)
            {
                await mediator.Publish(
                    new DeliveryDateChangedEvent(
                        lineId, oldDeliveryDate.Value, request.RequestedDeliveryDate.Value, userId),
                    cancellationToken);
            }
        }
    }
}
