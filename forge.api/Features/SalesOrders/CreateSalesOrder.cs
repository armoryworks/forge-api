using FluentValidation;
using MediatR;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders;

public record CreateSalesOrderCommand(
    int CustomerId,
    int? QuoteId,
    int? ShippingAddressId,
    int? BillingAddressId,
    string? CreditTerms,
    DateTimeOffset? RequestedDeliveryDate,
    string? CustomerPO,
    string? Notes,
    decimal TaxRate,
    List<CreateSalesOrderLineModel> Lines) : IRequest<SalesOrderListItemModel>;

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            // Phase 3 / WU-10 — fractional quantity allowed; zero / negative not.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
        });
    }
}

public class CreateSalesOrderHandler(
    ISalesOrderRepository repo,
    ICustomerRepository customerRepo,
    IPartRepository partRepo,
    IBarcodeService barcodeService)
    : IRequestHandler<CreateSalesOrderCommand, SalesOrderListItemModel>
{
    public async Task<SalesOrderListItemModel> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken);
        // Phase 3 H2 / WU-12: customer-active check mirrors the vendor → PO
        // gate that Phase 1 found missing.
        ActiveCheck.EnsureActive(customer, "Customer", "customerId", request.CustomerId);

        var orderNumber = await repo.GenerateNextOrderNumberAsync(cancellationToken);

        CreditTerms? creditTerms = request.CreditTerms != null
            ? Enum.Parse<CreditTerms>(request.CreditTerms, true)
            : null;

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            QuoteId = request.QuoteId,
            ShippingAddressId = request.ShippingAddressId,
            BillingAddressId = request.BillingAddressId,
            CreditTerms = creditTerms,
            RequestedDeliveryDate = request.RequestedDeliveryDate,
            CustomerPO = request.CustomerPO,
            Notes = request.Notes,
            TaxRate = request.TaxRate,
        };

        var lineNumber = 1;
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            // Phase 3 H2 / WU-12: part-active check on SO line. Lines are
            // permitted with null / zero PartId in some flows (free-form
            // service line); only enforce the active-check when a part is
            // actually referenced.
            if (line.PartId is int partId && partId > 0)
            {
                var part = await partRepo.FindAsync(partId, cancellationToken);
                ActiveCheck.EnsureActive(part, "Part", $"lines[{i}].partId", partId);
            }

            order.Lines.Add(new SalesOrderLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
            });
        }

        await repo.AddAsync(order, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.SalesOrder, order.Id, order.OrderNumber, cancellationToken);

        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new SalesOrderListItemModel(
            order.Id, order.OrderNumber, order.CustomerId, customer.Name,
            order.Status.ToString(), order.CustomerPO, order.Lines.Count,
            total, order.RequestedDeliveryDate, order.CreatedAt,
            SalesOrderId: order.Id, JobId: null);
    }
}
