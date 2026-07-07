using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// Post-lock change control: once an order leaves Draft its lines are
/// immutable, so edge-case alterations are captured as a NEW linked Draft
/// order carrying only the delta — never by mutating the locked record
/// (external QA / dev-review item). The addendum copies the commercial header
/// (customer, addresses, tax, PO, terms), starts with no lines, and flows
/// through the normal Draft → Confirm lifecycle.
/// </summary>
public record CreateAddendumOrderCommand(int ParentSalesOrderId) : IRequest<SalesOrderListItemModel>;

public class CreateAddendumOrderValidator : AbstractValidator<CreateAddendumOrderCommand>
{
    public CreateAddendumOrderValidator()
    {
        RuleFor(x => x.ParentSalesOrderId).GreaterThan(0);
    }
}

public class CreateAddendumOrderHandler(AppDbContext db)
    : IRequestHandler<CreateAddendumOrderCommand, SalesOrderListItemModel>
{
    public async Task<SalesOrderListItemModel> Handle(
        CreateAddendumOrderCommand request, CancellationToken cancellationToken)
    {
        var parent = await db.SalesOrders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == request.ParentSalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.ParentSalesOrderId} not found");

        if (parent.Status is SalesOrderStatus.Draft)
            throw new InvalidOperationException(
                "A Draft order is still editable — modify it directly instead of creating an addendum.");
        if (parent.Status is SalesOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot create an addendum for a cancelled order.");
        if (parent.ParentSalesOrderId is not null)
            throw new InvalidOperationException(
                "Cannot create an addendum of an addendum — chain them off the original order.");

        var nextNumber = await db.SalesOrders
            .Where(o => o.ParentSalesOrderId == parent.Id)
            .Select(o => (int?)o.AddendumNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var addendum = new SalesOrder
        {
            // Suffix numbering keeps the paper trail obvious on the floor:
            // SO-00042-A1, SO-00042-A2, …
            OrderNumber = $"{parent.OrderNumber}-A{nextNumber + 1}",
            CustomerId = parent.CustomerId,
            QuoteId = parent.QuoteId,
            ShippingAddressId = parent.ShippingAddressId,
            BillingAddressId = parent.BillingAddressId,
            TaxRate = parent.TaxRate,
            CustomerPO = parent.CustomerPO,
            CreditTerms = parent.CreditTerms,
            ParentSalesOrderId = parent.Id,
            AddendumNumber = nextNumber + 1,
        };

        db.SalesOrders.Add(addendum);

        db.LogActivityAt(
            "addendum-created",
            $"Addendum {addendum.OrderNumber} created for {parent.OrderNumber}",
            ("SalesOrder", parent.Id));

        await db.SaveChangesAsync(cancellationToken);

        return new SalesOrderListItemModel(
            addendum.Id, addendum.OrderNumber, addendum.CustomerId, parent.Customer.Name,
            addendum.Status.ToString(), addendum.CustomerPO, 0,
            0m, null, addendum.CreatedAt,
            SalesOrderId: addendum.Id, JobId: null);
    }
}
