using System.Security.Claims;

using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

public record UpdateSalesOrderCommand(
    int Id,
    int? ShippingAddressId,
    int? BillingAddressId,
    string? CreditTerms,
    DateTimeOffset? RequestedDeliveryDate,
    string? CustomerPO,
    string? Notes,
    decimal? TaxRate,
    // Optional editable order number — see UpdateSalesOrderRequestModel.OrderNumber.
    string? OrderNumber = null) : IRequest;

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
        RuleFor(x => x.OrderNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.OrderNumber));
    }
}

public class UpdateSalesOrderHandler(
    ISalesOrderRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext)
    : IRequestHandler<UpdateSalesOrderCommand>
{
    // System setting that gates caller-supplied order numbers (shared with CreateSalesOrder).
    private const string AllowManualOrderNumbersKey = "sales_orders.allow_manual_numbers";

    public async Task Handle(UpdateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.Id} not found");

        if (order.Status != SalesOrderStatus.Draft && order.Status != SalesOrderStatus.Confirmed)
            throw new InvalidOperationException("Can only update Draft or Confirmed sales orders");

        // User-settable order number — Draft-only (the number is on customer-facing
        // documents once the order leaves Draft), gated by the manual-numbers setting,
        // and uniqueness-checked (excluding this order). The DB unique index is the
        // final backstop.
        var orderNumberChanged = false;
        if (request.OrderNumber is not null)
        {
            var newNumber = request.OrderNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, order.OrderNumber, StringComparison.Ordinal))
            {
                if (order.Status != SalesOrderStatus.Draft)
                    throw new InvalidOperationException(
                        "This sales order's number can only be changed while it is Draft.");
                if (!await ManualOrderNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual sales order numbers are disabled. Turn on 'sales_orders.allow_manual_numbers' in settings to change an order number.");
                if (await repo.OrderNumberExistsAsync(newNumber, order.Id, cancellationToken))
                    throw new InvalidOperationException($"Sales order number '{newNumber}' is already in use.");
                // Record the rename in the identifier registry: ensure the current number is on record
                // (covers pre-registry orders), then supersede it — the old number stays resolvable.
                await identifiers.IssueAsync(BusinessEntityType.SalesOrder, order.Id, order.OrderNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.SalesOrder, order.Id, newNumber, cancellationToken);
                order.OrderNumber = newNumber;
                orderNumberChanged = true;
            }
        }

        var oldDeliveryDate = order.RequestedDeliveryDate;

        if (request.ShippingAddressId.HasValue) order.ShippingAddressId = request.ShippingAddressId;
        if (request.BillingAddressId.HasValue) order.BillingAddressId = request.BillingAddressId;
        if (request.CreditTerms != null) order.CreditTerms = Enum.Parse<CreditTerms>(request.CreditTerms, true);
        if (request.RequestedDeliveryDate.HasValue) order.RequestedDeliveryDate = request.RequestedDeliveryDate;
        if (request.CustomerPO != null) order.CustomerPO = request.CustomerPO;
        if (request.Notes != null) order.Notes = request.Notes;
        if (request.TaxRate.HasValue) order.TaxRate = request.TaxRate.Value;

        if (orderNumberChanged)
            db.LogActivityAt("updated", $"Order number changed to {order.OrderNumber}", ("SalesOrder", order.Id));

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

    private async Task<bool> ManualOrderNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualOrderNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
