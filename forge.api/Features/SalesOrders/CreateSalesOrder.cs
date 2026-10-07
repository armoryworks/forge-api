using FluentValidation;
using FluentValidation.Results;
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
    List<CreateSalesOrderLineModel> Lines,
    // Optional caller-supplied order number — see CreateSalesOrderRequestModel.OrderNumber.
    string? OrderNumber = null) : IRequest<SalesOrderListItemModel>;

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        // Matches the sales_orders.order_number column (varchar(20)). Uniqueness is
        // checked in the handler since it needs a DB lookup.
        RuleFor(x => x.OrderNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.OrderNumber));
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
    IBarcodeService barcodeService,
    ICustomerAddressRepository addressRepo,
    // Optional/null-default so isolated unit-test constructions stay valid; DI supplies both.
    ISystemSettingRepository? systemSettings = null,
    IBusinessIdentifierService? identifiers = null)
    : IRequestHandler<CreateSalesOrderCommand, SalesOrderListItemModel>
{
    // System setting that gates caller-supplied order numbers. Stored as "true"/"false".
    private const string AllowManualOrderNumbersKey = "sales_orders.allow_manual_numbers";

    public async Task<SalesOrderListItemModel> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken);
        // Phase 3 H2 / WU-12: customer-active check mirrors the vendor → PO
        // gate that Phase 1 found missing.
        ActiveCheck.EnsureActive(customer, "Customer", "customerId", request.CustomerId);

        var orderNumber = await ResolveOrderNumberAsync(request, cancellationToken);

        var shippingAddressId = await ResolveAddressIdAsync(
            request.CustomerId, request.ShippingAddressId, AddressType.Shipping, "shippingAddressId", cancellationToken);
        var billingAddressId = await ResolveAddressIdAsync(
            request.CustomerId, request.BillingAddressId, AddressType.Billing, "billingAddressId", cancellationToken);

        CreditTerms? creditTerms = request.CreditTerms != null
            ? Enum.Parse<CreditTerms>(request.CreditTerms, true)
            : null;

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            QuoteId = request.QuoteId,
            ShippingAddressId = shippingAddressId,
            BillingAddressId = billingAddressId,
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

        // Record the number in the identifier registry (history + resolution).
        if (identifiers is not null)
            await identifiers.IssueAsync(BusinessEntityType.SalesOrder, order.Id, order.OrderNumber, cancellationToken);

        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new SalesOrderListItemModel(
            order.Id, order.OrderNumber, order.CustomerId, customer.Name,
            order.Status.ToString(), order.CustomerPO, order.Lines.Count,
            total, order.RequestedDeliveryDate, order.CreatedAt,
            SalesOrderId: order.Id, JobId: null);
    }

    // Uses a caller-supplied order number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential "SO" number.
    private async Task<string> ResolveOrderNumberAsync(CreateSalesOrderCommand request, CancellationToken ct)
    {
        var supplied = request.OrderNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualOrderNumbersAllowedAsync(ct))
        {
            if (await repo.OrderNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Sales order number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GenerateNextOrderNumberAsync(ct);
    }

    private async Task<int?> ResolveAddressIdAsync(
        int customerId, int? suppliedId, AddressType addressType, string fieldPath, CancellationToken ct)
    {
        if (suppliedId is int id)
        {
            var address = await addressRepo.FindAsync(id, ct);
            if (address is null || address.CustomerId != customerId)
                throw AddressRejected(fieldPath, id, $"Address {id} does not belong to this customer.");
            if (!address.IsActive)
                throw AddressRejected(fieldPath, id, $"Address {id} is inactive.");
            if (address.AddressType != addressType && address.AddressType != AddressType.Both)
                throw AddressRejected(fieldPath, id, $"Address {id} is a {address.AddressType} address, not a {addressType} address.");
            return id;
        }

        var defaults = (await addressRepo.GetByCustomerAsync(customerId, ct))
            .Where(a => a.IsActive && a.IsDefault)
            .ToList();
        var typeName = addressType.ToString();
        return (defaults.FirstOrDefault(a => a.AddressType == typeName)
            ?? defaults.FirstOrDefault(a => a.AddressType == nameof(AddressType.Both)))?.Id;
    }

    private static ValidationException AddressRejected(string fieldPath, int id, string message) =>
        new(new[] { new ValidationFailure(fieldPath, message) { AttemptedValue = id } });

    private async Task<bool> ManualOrderNumbersAllowedAsync(CancellationToken ct)
    {
        if (systemSettings is null) return false;
        var setting = await systemSettings.FindByKeyAsync(AllowManualOrderNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
