using System.Security.Claims;

using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
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
    string? OrderNumber = null,
    int? CurrencyId = null,
    decimal? FxRate = null,
    FreightTerms? FreightTerms = null,
    bool? ClearFreightTerms = null,
    int? PaymentTermsId = null,
    TaxOverrideReason? TaxOverrideReason = null,
    string? TaxOverrideNote = null) : IRequest;

public class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId.HasValue);
        RuleFor(x => x.BillingAddressId).GreaterThan(0).When(x => x.BillingAddressId.HasValue);
        RuleFor(x => x.CreditTerms)
            .Must(t => CreditTermsExtensions.TryParseLenient(t, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CreditTerms))
            .WithMessage(CreditTermsExtensions.AllowedValuesMessage);
        RuleFor(x => x.CustomerPO).MaximumLength(100).When(x => x.CustomerPO is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 1).When(x => x.TaxRate.HasValue);
        RuleFor(x => x.OrderNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.OrderNumber));
        RuleFor(x => x.FxRate).GreaterThan(0m).When(x => x.FxRate.HasValue);
    }
}

public class UpdateSalesOrderHandler(
    ISalesOrderRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    IMediator mediator,
    IHttpContextAccessor httpContext,
    Forge.Api.Services.TaxOverrideGuard? taxGuard = null)
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
                        "Manual sales order numbers are turned off, so the order number can't be changed. Ask an admin to turn them on in Admin > Settings > Numbering.");
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

        var currency = await new DocumentCurrencyResolver(db).ResolveChangeAsync(
            order.CurrencyId, order.FxRate, request.CurrencyId, request.FxRate, order.CreatedAt, cancellationToken);
        if (currency is not null)
        {
            if (order.Status != SalesOrderStatus.Draft)
                throw new CurrencyLockedException(
                    CurrencyLockedException.DocumentLocked,
                    "A sales order's currency and exchange rate can only change while it is Draft.");
            order.CurrencyId = currency.CurrencyId;
            order.FxRate = currency.FxRate;
            db.LogActivityAt("updated", $"Order currency set to {currency.Code ?? currency.CurrencyId.ToString()} at {currency.FxRate}", ("SalesOrder", order.Id));
        }

        if (request.ShippingAddressId is int shippingId && shippingId != order.ShippingAddressId)
        {
            await EnsureAddressUsableAsync(order.CustomerId, shippingId, AddressType.Shipping, "shippingAddressId", cancellationToken);
            order.ShippingAddressId = shippingId;
        }
        if (request.BillingAddressId is int billingId && billingId != order.BillingAddressId)
        {
            await EnsureAddressUsableAsync(order.CustomerId, billingId, AddressType.Billing, "billingAddressId", cancellationToken);
            order.BillingAddressId = billingId;
        }
        if (!string.IsNullOrWhiteSpace(request.CreditTerms) && !CreditTermsExtensions.TryParseLenient(request.CreditTerms, out _))
            throw new ValidationException(CreditTermsExtensions.AllowedValuesMessage);
        var (termsAsked, paymentTerms, legacyTerms) = await new TermsChoice(request.PaymentTermsId, request.CreditTerms, false)
            .ResolveAsync(new PaymentTermsResolver(db), order.PaymentTermsId, nameof(request.PaymentTermsId), cancellationToken);
        if (termsAsked)
        {
            order.PaymentTermsId = paymentTerms?.Id;
            order.CreditTerms = legacyTerms;
        }
        if (request.ClearFreightTerms == true) order.FreightTerms = null;
        else if (request.FreightTerms is { } freightTerms)
        {
            if (!Enum.IsDefined(freightTerms))
                throw new ValidationException("Unknown freight terms.");
            order.FreightTerms = freightTerms;
        }
        if (request.RequestedDeliveryDate.HasValue) order.RequestedDeliveryDate = request.RequestedDeliveryDate;
        if (request.CustomerPO != null) order.CustomerPO = request.CustomerPO;
        if (request.Notes != null) order.Notes = request.Notes;
        if (request.TaxRate.HasValue)
        {
            var rateChanged = TaxOverrideGuard.IsOverride(request.TaxRate.Value, order.TaxRate);
            if (taxGuard is not null && (rateChanged || request.TaxOverrideReason is not null))
            {
                var defaultRate = await taxGuard.GetComputedRateAsync(order.CustomerId, cancellationToken);
                var taxOverride = await taxGuard.EnsureCanOverrideAsync(
                    order.CustomerId, request.TaxRate.Value, defaultRate,
                    request.TaxOverrideReason, request.TaxOverrideNote, "sales_order", cancellationToken);
                order.TaxOverrideReason = taxOverride?.Reason;
                order.TaxOverrideNote = taxOverride?.Note;
                if (taxOverride is not null)
                    db.LogActivityAt("tax-override", TaxOverrideActivity.Describe(request.TaxRate.Value, taxOverride), ("SalesOrder", order.Id));
            }
            order.TaxRate = request.TaxRate.Value;
        }

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

    private async Task EnsureAddressUsableAsync(
        int customerId, int id, AddressType addressType, string fieldPath, CancellationToken ct)
    {
        var address = await db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        SalesOrderAddressRules.EnsureUsable(address, id, customerId, addressType, fieldPath);
    }

    private async Task<bool> ManualOrderNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualOrderNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
