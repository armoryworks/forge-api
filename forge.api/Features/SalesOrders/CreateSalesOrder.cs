using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.Freight;
using Forge.Api.Services.Fx;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

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
    string? OrderNumber = null,
    int? CurrencyId = null,
    decimal? FxRate = null,
    FreightTerms? FreightTerms = null,
    int? PaymentTermsId = null,
    TaxOverrideReason? TaxOverrideReason = null,
    string? TaxOverrideNote = null) : IRequest<SalesOrderListItemModel>;

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.CreditTerms)
            .Must(t => CreditTermsExtensions.TryParseLenient(t, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CreditTerms))
            .WithMessage(CreditTermsExtensions.AllowedValuesMessage);
        // Matches the sales_orders.order_number column (varchar(20)). Uniqueness is
        // checked in the handler since it needs a DB lookup.
        RuleFor(x => x.OrderNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.OrderNumber));
        RuleFor(x => x.FxRate).GreaterThan(0m).When(x => x.FxRate.HasValue);
        RuleFor(x => x.FreightTerms).IsInEnum().When(x => x.FreightTerms.HasValue);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineType).IsInEnum();
            line.RuleFor(l => l.Description).NotEmpty().When(l => l.LineType != DocumentLineType.Freight);
            line.RuleFor(l => l.PartId).Null()
                .When(l => l.LineType == DocumentLineType.Freight)
                .WithMessage("A freight line can't name a part.")
                .WithErrorCode(FreightLines.PartCode);
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
    IBusinessIdentifierService? identifiers = null,
    Forge.Api.Services.TaxOverrideGuard? taxGuard = null,
    AppDbContext? db = null,
    IClock? clock = null)
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

        Forge.Api.Services.TaxOverrideStamp? taxOverride = null;
        if (taxGuard is not null)
        {
            var defaultRate = await taxGuard.GetDefaultRateAsync(request.CustomerId, cancellationToken);
            taxOverride = await taxGuard.EnsureCanOverrideAsync(
                request.CustomerId, request.TaxRate, defaultRate,
                request.TaxOverrideReason, request.TaxOverrideNote, "sales_order", cancellationToken);
        }

        var orderNumber = await ResolveOrderNumberAsync(request, cancellationToken);

        var shippingAddressId = await ResolveAddressIdAsync(
            request.CustomerId, request.ShippingAddressId, AddressType.Shipping, "shippingAddressId", cancellationToken);
        var billingAddressId = await ResolveAddressIdAsync(
            request.CustomerId, request.BillingAddressId, AddressType.Billing, "billingAddressId", cancellationToken);

        CreditTerms? creditTerms = customer.DefaultCreditTerms;
        var paymentTermsId = customer.PaymentTermsId;
        if (!string.IsNullOrWhiteSpace(request.CreditTerms))
        {
            if (!CreditTermsExtensions.TryParseLenient(request.CreditTerms, out var requestedTerms))
                throw new ValidationException(CreditTermsExtensions.AllowedValuesMessage);
            creditTerms = requestedTerms;
            paymentTermsId = null;
        }
        if (db is not null)
        {
            var (termsAsked, paymentTerms, legacyTerms) = await new TermsChoice(request.PaymentTermsId, request.CreditTerms, false)
                .ResolveAsync(new PaymentTermsResolver(db), null, nameof(request.PaymentTermsId), cancellationToken);
            if (termsAsked)
            {
                paymentTermsId = paymentTerms?.Id;
                creditTerms = legacyTerms;
            }
            else if (paymentTermsId is null && creditTerms is not null)
                paymentTermsId = (await new PaymentTermsResolver(db).ForLegacyAsync(creditTerms, cancellationToken))?.Id;
        }

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            QuoteId = request.QuoteId,
            ShippingAddressId = shippingAddressId,
            BillingAddressId = billingAddressId,
            CreditTerms = creditTerms,
            PaymentTermsId = paymentTermsId,
            RequestedDeliveryDate = request.RequestedDeliveryDate,
            CustomerPO = request.CustomerPO,
            Notes = request.Notes,
            TaxRate = request.TaxRate,
            TaxOverrideReason = taxOverride?.Reason,
            TaxOverrideNote = taxOverride?.Note,
            FreightTerms = request.FreightTerms ?? customer.FreightTerms,
        };

        if (db is not null)
        {
            var quoteCurrencyId = request.QuoteId is int quoteId
                ? await db.Quotes.AsNoTracking().Where(q => q.Id == quoteId).Select(q => q.CurrencyId).FirstOrDefaultAsync(cancellationToken)
                : null;
            var currency = await new DocumentCurrencyResolver(db).ResolveAsync(
                request.CurrencyId, request.FxRate, quoteCurrencyId ?? customer!.CurrencyId,
                (clock ?? new Forge.Integrations.SystemClock()).UtcNow, cancellationToken);
            order.CurrencyId = currency.CurrencyId;
            order.FxRate = currency.FxRate;
        }

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

            var isFreight = line.LineType == DocumentLineType.Freight;
            order.Lines.Add(new SalesOrderLine
            {
                PartId = line.PartId,
                Description = isFreight && string.IsNullOrWhiteSpace(line.Description) ? FreightLines.DefaultDescription : line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
                LineType = line.LineType,
                TaxCode = !isFreight ? null
                    : db is null ? (line.Taxable == true ? FreightLines.TaxableCode : InvoiceLine.NonTaxableCode)
                    : await FreightLines.TaxCodeForAsync(db, request.CustomerId, (clock ?? new Forge.Integrations.SystemClock()).UtcNow, line.Taxable, cancellationToken),
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
            SalesOrderId: order.Id, JobId: null,
            CurrencyId: order.CurrencyId, FxRate: order.FxRate, TotalFunctional: FxMath.ToFunctional(total, order.FxRate));
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
            SalesOrderAddressRules.EnsureUsable(address, id, customerId, addressType, fieldPath);
            return id;
        }

        var defaults = (await addressRepo.GetByCustomerAsync(customerId, ct))
            .Where(a => a.IsActive && a.IsDefault)
            .ToList();
        var typeName = addressType.ToString();
        return (defaults.FirstOrDefault(a => a.AddressType == typeName)
            ?? defaults.FirstOrDefault(a => a.AddressType == nameof(AddressType.Both)))?.Id;
    }

    private async Task<bool> ManualOrderNumbersAllowedAsync(CancellationToken ct)
    {
        if (systemSettings is null) return false;
        var setting = await systemSettings.FindByKeyAsync(AllowManualOrderNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
