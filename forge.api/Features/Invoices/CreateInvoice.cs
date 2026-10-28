using System.Globalization;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Freight;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Invoices;

public record CreateInvoiceCommand(
    int CustomerId,
    int? SalesOrderId,
    int? ShipmentId,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    string? CreditTerms,
    decimal TaxRate,
    string? Notes,
    List<CreateInvoiceLineModel> Lines,
    string? CustomerPO = null,
    // Multi-currency (Phase-4 FULLGL, additive). Null CurrencyId → the active book's functional currency;
    // FxRate is the booking rate (txn→functional). Defaults keep single-currency callers byte-for-byte unchanged.
    int? CurrencyId = null,
    decimal? FxRate = null,
    // Optional caller-supplied invoice number — see CreateInvoiceRequestModel.InvoiceNumber.
    string? InvoiceNumber = null,
    int? SalesTaxRateId = null,
    int? PaymentTermsId = null,
    bool ApplyCustomerDeposits = true,
    TaxOverrideReason? TaxOverrideReason = null,
    string? TaxOverrideNote = null,
    bool EnforceTaxOverride = false) : IRequest<InvoiceListItemModel>;

public class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate);
        RuleFor(x => x.CreditTerms)
            .Must(t => CreditTermsExtensions.TryParseLenient(t, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CreditTerms))
            .WithMessage(CreditTermsExtensions.AllowedValuesMessage);
        RuleFor(x => x.DueDate)
            .Must((x, due) => !CreditTermsExtensions.TryParseLenient(x.CreditTerms, out var terms)
                || terms.MatchesDueDate(x.InvoiceDate, due))
            .When(x => !string.IsNullOrWhiteSpace(x.CreditTerms))
            .WithMessage(x => CreditTermsExtensions.TryParseLenient(x.CreditTerms, out var terms)
                ? $"Due date must be {terms.NetDays()} days after the invoice date for {terms} terms."
                : CreditTermsExtensions.AllowedValuesMessage);
        RuleFor(x => x.CustomerPO).MaximumLength(50);
        RuleFor(x => x.InvoiceNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.InvoiceNumber));
        // FX booking rate must be positive (a 0 or negative rate would zero/invert the functional amount).
        RuleFor(x => x.FxRate).GreaterThan(0m).When(x => x.FxRate.HasValue);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineType).IsInEnum();
            line.RuleFor(l => l.LineType).NotEqual(DocumentLineType.Deposit)
                .WithMessage("Deposit lines are added from the sales order's deposit invoices.")
                .WithErrorCode(CustomerDeposits.ManualLineCode);
            line.RuleFor(l => l.Description).NotEmpty().When(l => l.LineType != DocumentLineType.Freight);
            line.RuleFor(l => l.Description).MaximumLength(500);
            line.RuleFor(l => l.PartId).Null()
                .When(l => l.LineType == DocumentLineType.Freight)
                .WithMessage("A freight line can't name a part.")
                .WithErrorCode(FreightLines.PartCode);
            // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateInvoiceHandler(
    IInvoiceRepository repo,
    ICustomerRepository customerRepo,
    AppDbContext db,
    // AUDIT-21-S1: optional / null-default so isolated unit-test constructions stay valid; the DI
    // path supplies both. The QBO enqueue self-gates on a provider being connected.
    IAccountingProviderFactory? accountingProviders = null,
    ISyncQueueRepository? syncQueue = null,
    // Optional / null-default (same reason): a manual-number override needs the setting repo, and the
    // identifier registry records the number for resolution/history. DI supplies both.
    ISystemSettingRepository? systemSettings = null,
    IBusinessIdentifierService? identifiers = null,
    IPublisher? publisher = null,
    TaxOverrideGuard? taxGuard = null)
    : IRequestHandler<CreateInvoiceCommand, InvoiceListItemModel>
{
    // System setting that gates caller-supplied invoice numbers. Stored as "true"/"false".
    private const string AllowManualInvoiceNumbersKey = "invoices.allow_manual_numbers";

    /// <summary>System setting naming the QuickBooks item freight lines sync to.</summary>
    public const string FreightItemSettingKey = "qbo.freight_item_id";

    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    public async Task<InvoiceListItemModel> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        string? salesOrderCustomerPo = null;
        CreditTerms? salesOrderTerms = null;
        int? salesOrderTermsId = null;
        int? salesOrderCurrencyId = null;
        (decimal Rate, TaxOverrideReason? Reason, string? Note)? salesOrderTax = null;
        var currencies = new DocumentCurrencyResolver(db);
        if (request.SalesOrderId is int linkedSoId)
        {
            var salesOrder = await db.SalesOrders.AsNoTracking()
                .Where(so => so.Id == linkedSoId)
                .Select(so => new { so.OrderNumber, so.CustomerId, so.CustomerPO, so.CreditTerms, so.PaymentTermsId, so.CurrencyId, so.TaxRate, so.TaxOverrideReason, so.TaxOverrideNote })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Sales order {linkedSoId} not found");

            if (salesOrder.CustomerId != request.CustomerId)
                throw new InvalidOperationException(
                    $"Sales order {salesOrder.OrderNumber} belongs to a different customer than {customer.Name}.");

            salesOrderCustomerPo = salesOrder.CustomerPO;
            salesOrderTerms = salesOrder.CreditTerms;
            salesOrderTermsId = salesOrder.PaymentTermsId;
            salesOrderTax = (salesOrder.TaxRate, salesOrder.TaxOverrideReason, salesOrder.TaxOverrideNote);
            salesOrderCurrencyId = await currencies.EffectiveCurrencyIdAsync(salesOrder.CurrencyId, cancellationToken);
            if (request.CurrencyId is int requestedCurrencyId && requestedCurrencyId != salesOrderCurrencyId)
                throw DocumentCurrencyResolver.Failure("CurrencyId", DocumentCurrencyResolver.InvoiceSoCurrencyMismatchCode,
                    $"An invoice for sales order {salesOrder.OrderNumber} must be in the order's currency ({await currencies.CodeForAsync(salesOrderCurrencyId.Value, cancellationToken)}).");
        }

        var linkedLineIds = request.Lines
            .Where(l => l.SalesOrderLineId != null)
            .Select(l => l.SalesOrderLineId!.Value)
            .Distinct()
            .ToList();
        if (linkedLineIds.Count > 0)
        {
            if (request.SalesOrderId is not int lineOrderId)
                throw new ValidationException("An invoice line can name a sales-order line only when the invoice is for that sales order.");

            var lineIdsOnOrder = await db.SalesOrderLines.AsNoTracking()
                .Where(l => l.SalesOrderId == lineOrderId && linkedLineIds.Contains(l.Id))
                .CountAsync(cancellationToken);
            if (lineIdsOnOrder != linkedLineIds.Count)
                throw new ValidationException("An invoice line names a sales-order line that is not on this invoice's sales order.");

            await EnsureSalesOrderFreightNotBilledAsync(request, cancellationToken);
        }

        if (request.ShipmentId is int shipmentId)
        {
            var shipment = await db.Shipments.AsNoTracking()
                .Where(s => s.Id == shipmentId)
                .Select(s => new { s.ShipmentNumber, s.SalesOrderId, s.SalesOrder.OrderNumber, s.SalesOrder.CustomerId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Shipment {shipmentId} not found");

            if (shipment.CustomerId != request.CustomerId)
                throw new InvalidOperationException(
                    $"Shipment {shipment.ShipmentNumber} belongs to a different customer than {customer.Name}.");

            if (request.SalesOrderId is int requestedSoId && shipment.SalesOrderId != requestedSoId)
                throw new InvalidOperationException(
                    $"Shipment {shipment.ShipmentNumber} belongs to sales order {shipment.OrderNumber}, not the one on this invoice.");

            // INV-IN2: one invoice per shipment (a unique index enforces it). Guard here
            // so a double-invoice returns a clean 409 instead of a DbUpdateException 500.
            var existingInvoiceNumber = await db.Invoices
                .Where(i => i.ShipmentId == shipmentId)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingInvoiceNumber is not null)
                throw new InvalidOperationException(
                    $"Shipment {shipment.ShipmentNumber} is already on invoice {existingInvoiceNumber}. Each shipment can be invoiced once.");
        }

        // AUDIT-P06-1 / Q2C-BE-8: you cannot invoice more than has shipped. When the invoice is tied
        // to a sales order, cap each part's cumulative invoiced quantity (existing invoices for the SO
        // + this one) at the quantity shipped for that SO. Lines with no PartId (e.g. freight) aren't
        // goods-shipment-bound and are skipped.
        if (request.SalesOrderId is int shippedSoId)
        {
            var shippedByPart = await db.ShipmentLines
                .Where(sl => sl.PartId != null
                    && sl.Shipment.SalesOrderId == shippedSoId
                    && ShippedStatuses.Contains(sl.Shipment.Status))
                .GroupBy(sl => sl.PartId!.Value)
                .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.PartId, x => x.Qty, cancellationToken);

            var requestedPartIds = request.Lines.Where(l => l.PartId != null).Select(l => l.PartId!.Value).Distinct().ToList();
            var orderedServiceByPart = await db.SalesOrderLines
                .Where(l => l.SalesOrderId == shippedSoId
                    && l.PartId != null
                    && requestedPartIds.Contains(l.PartId.Value)
                    && l.Part!.InventoryClass == InventoryClass.Service)
                .GroupBy(l => l.PartId!.Value)
                .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.PartId, x => x.Qty, cancellationToken);
            foreach (var (partId, ordered) in orderedServiceByPart)
                shippedByPart[partId] = ordered;

            var invoicedByPart = await db.InvoiceLines
                .Where(il => il.PartId != null && il.Invoice.SalesOrderId == shippedSoId
                    && il.Invoice.Status != InvoiceStatus.Voided)
                .GroupBy(il => il.PartId!.Value)
                .Select(g => new { PartId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.PartId, x => x.Qty, cancellationToken);

            var overInvoiced = request.Lines
                .Where(l => l.PartId != null)
                .GroupBy(l => l.PartId!.Value)
                .Select(g => new
                {
                    PartId = g.Key,
                    Requested = g.Sum(l => l.Quantity),
                    Shipped = shippedByPart.GetValueOrDefault(g.Key),
                    Already = invoicedByPart.GetValueOrDefault(g.Key),
                })
                .Where(x => x.Already + x.Requested > x.Shipped)
                .ToList();

            if (overInvoiced.Count > 0)
            {
                var overInvoicedPartIds = overInvoiced.Select(x => x.PartId).ToList();
                var partNumbers = await db.Parts
                    .Where(p => overInvoicedPartIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.PartNumber, cancellationToken);

                throw new InvalidOperationException(string.Join(" ", overInvoiced.Select(x =>
                    $"Can't invoice {Qty(x.Requested)} of {partNumbers.GetValueOrDefault(x.PartId) ?? $"part {x.PartId}"}: " +
                    $"{Qty(x.Shipped)} {(orderedServiceByPart.ContainsKey(x.PartId) ? "ordered" : "shipped")}, {Qty(x.Already)} already invoiced, " +
                    $"{Qty(Math.Max(0m, x.Shipped - x.Already))} left to invoice.")));
            }
        }

        var invoiceNumber = await ResolveInvoiceNumberAsync(request, cancellationToken);

        var termsResolver = new PaymentTermsResolver(db);
        PaymentTerm? paymentTerms;
        CreditTerms? creditTerms;
        if (request.PaymentTermsId is int pickedTermsId)
        {
            paymentTerms = await termsResolver.RequireAsync(pickedTermsId, nameof(request.PaymentTermsId), cancellationToken);
            if (!EarlyPaymentDiscount.MatchesDueDate(request.InvoiceDate, request.DueDate, paymentTerms.NetDays))
                throw PaymentTermsResolver.Failure(nameof(request.DueDate), PaymentTermsResolver.DueDateCode,
                    $"Due date must be {paymentTerms.NetDays} days after the invoice date for {paymentTerms.Name} terms.");
            creditTerms = paymentTerms.LegacyCreditTerms;
        }
        else if (!string.IsNullOrWhiteSpace(request.CreditTerms))
        {
            if (!CreditTermsExtensions.TryParseLenient(request.CreditTerms, out var requestedTerms))
                throw new ValidationException(CreditTermsExtensions.AllowedValuesMessage);
            creditTerms = requestedTerms;
            paymentTerms = await termsResolver.ForLegacyAsync(requestedTerms, cancellationToken);
        }
        else
        {
            var inheritedTerms = request.SalesOrderId is not null && (salesOrderTermsId is not null || salesOrderTerms is not null)
                ? await termsResolver.EffectiveAsync(salesOrderTermsId, salesOrderTerms, cancellationToken)
                : await termsResolver.EffectiveAsync(customer.PaymentTermsId, customer.DefaultCreditTerms, cancellationToken);
            paymentTerms = inheritedTerms is not null
                && EarlyPaymentDiscount.MatchesDueDate(request.InvoiceDate, request.DueDate, inheritedTerms.NetDays)
                    ? inheritedTerms
                    : null;
            var inherited = salesOrderTerms ?? customer.DefaultCreditTerms;
            creditTerms = paymentTerms is not null
                ? paymentTerms.LegacyCreditTerms
                : inherited is { } terms && terms.MatchesDueDate(request.InvoiceDate, request.DueDate)
                    ? terms
                    : null;
        }

        // Propagate CustomerPO from the sourcing SO when the caller didn't
        // override. B2B customers reject invoices that don't echo their PO #.
        var customerPo = request.CustomerPO ?? salesOrderCustomerPo;

        var currency = await currencies.ResolveAsync(
            request.CurrencyId, request.FxRate, salesOrderCurrencyId ?? customer.CurrencyId, request.InvoiceDate, cancellationToken);

        var invoiceDay = DateOnly.FromDateTime(request.InvoiceDate.UtcDateTime);
        var customerExempt = customer.IsTaxExemptOn(invoiceDay);
        var jurisdiction = await ResolveJurisdictionAsync(request, cancellationToken);

        TaxOverrideStamp? taxOverride = null;
        var taxOverrideIsNew = false;
        if (!customerExempt && salesOrderTax is { } orderTax && !TaxOverrideGuard.IsOverride(request.TaxRate, orderTax.Rate))
        {
            taxOverride = orderTax.Reason is { } orderReason ? new TaxOverrideStamp(orderReason, orderTax.Note, null) : null;
        }
        else if (!customerExempt && request.EnforceTaxOverride && taxGuard is not null)
        {
            var computed = await GetTaxRateForCustomerHandler.ResolveAsync(db, request.CustomerId, request.InvoiceDate, cancellationToken);
            taxOverride = await taxGuard.EnsureCanOverrideAsync(
                request.CustomerId, request.TaxRate, computed?.Rate ?? 0m,
                request.TaxOverrideReason, request.TaxOverrideNote, "invoice", cancellationToken);
            taxOverrideIsNew = taxOverride is not null;
        }

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = request.CustomerId,
            CurrencyId = currency.CurrencyId,
            FxRate = currency.FxRate,
            SalesOrderId = request.SalesOrderId,
            ShipmentId = request.ShipmentId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CreditTerms = creditTerms,
            TaxRate = customerExempt ? 0m : request.TaxRate,
            SalesTaxRateId = jurisdiction?.Id,
            TaxOverrideReason = taxOverride?.Reason,
            TaxOverrideNote = taxOverride?.Note,
            Notes = request.Notes,
            CustomerPO = customerPo,
        };
        if (paymentTerms is not null)
            PaymentTermsResolver.Stamp(invoice, paymentTerms);

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            var isFreight = line.LineType == DocumentLineType.Freight;
            invoice.Lines.Add(new InvoiceLine
            {
                PartId = line.PartId,
                Description = isFreight && string.IsNullOrWhiteSpace(line.Description) ? FreightLines.DefaultDescription : line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                LineType = line.LineType,
                TaxCode = isFreight
                    ? line.Taxable switch
                    {
                        true => FreightLines.TaxableCode,
                        false => InvoiceLine.NonTaxableCode,
                        null => FreightLines.DefaultTaxCode(jurisdiction?.FreightTaxable ?? false, customerExempt),
                    }
                    : line.Taxable == false ? InvoiceLine.NonTaxableCode : null,
                SalesOrderLineId = line.SalesOrderLineId,
            });
        }

        if (request.ApplyCustomerDeposits)
            await CustomerDeposits.ApplyAsync(db, invoice, cancellationToken);

        await repo.AddAsync(invoice, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Created invoice {invoice.InvoiceNumber} for {customer.Name}", ("Invoice", invoice.Id));
        if (taxOverrideIsNew)
            db.LogActivityAt("tax-override", TaxOverrideActivity.Describe(invoice.TaxRate, taxOverride!), ("Invoice", invoice.Id));
        await db.SaveChangesAsync(cancellationToken);

        // Record the number in the identifier registry (history + resolution).
        if (identifiers is not null)
            await identifiers.IssueAsync(BusinessEntityType.Invoice, invoice.Id, invoice.InvoiceNumber, cancellationToken);

        var total = invoice.Total;

        // AUDIT-21-S1 (BLOCKER): enqueue a QBO sync row so AR reaches the accounting provider —
        // previously only MoveJobStage enqueued, leaving invoices created any other way invisible to
        // sync. Only when a provider is actually connected (mirrors MoveJobStage); unlike the job path
        // we do NOT require the customer to be pre-synced — the sync worker upserts the customer, so a
        // first invoice for a brand-new customer still syncs.
        if (accountingProviders is not null && syncQueue is not null
            && await accountingProviders.GetConnectedExternalProviderAsync(cancellationToken) is not null)
        {
            var freightItemId = invoice.Lines.Any(l => l.IsFreight) && systemSettings is not null
                ? (await systemSettings.FindByKeyAsync(FreightItemSettingKey, cancellationToken))?.Value is { Length: > 0 } itemId ? itemId : null
                : null;
            var document = new AccountingDocument(
                Type: AccountingDocumentType.Invoice,
                CustomerExternalId: customer.ExternalId ?? string.Empty,
                LineItems: invoice.Lines
                    .Select(l => l.IsFreight
                        ? new AccountingLineItem(l.Description, l.Quantity, l.UnitPrice, freightItemId, IsFreight: true, Taxable: l.IsTaxable)
                        : new AccountingLineItem(l.Description, l.Quantity, l.UnitPrice, ItemExternalId: null))
                    .ToList(),
                RefNumber: invoice.InvoiceNumber,
                Amount: total,
                Date: invoice.InvoiceDate,
                CurrencyCode: currency.IsFunctional ? null : currency.Code,
                ExchangeRate: currency.IsFunctional ? null : currency.FxRate,
                Terms: paymentTerms is null
                    ? null
                    : new AccountingTerms(paymentTerms.Name, paymentTerms.NetDays, paymentTerms.HasDiscount ? paymentTerms.DiscountPercent : 0m, paymentTerms.HasDiscount ? paymentTerms.DiscountDays : 0));
            await syncQueue.EnqueueAsync("Invoice", invoice.Id, "CreateInvoice",
                JsonSerializer.Serialize(document), cancellationToken);
        }

        if (publisher is not null)
            await publisher.Publish(new InvoiceCreatedEvent(invoice.Id, invoice.InvoiceNumber, invoice.CustomerId, total), cancellationToken);

        var currencyCode = await db.Currencies.AsNoTracking()
            .Where(c => c.Id == invoice.CurrencyId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
        return new InvoiceListItemModel(
            invoice.Id, invoice.InvoiceNumber, invoice.CustomerId, customer.Name,
            invoice.Status.ToString(), invoice.InvoiceDate, invoice.DueDate,
            total, 0, total, invoice.CreatedAt, currencyCode,
            invoice.CurrencyId, invoice.FxRate, FxMath.ToFunctional(total, invoice.FxRate),
            invoice.PaymentTermsId, invoice.DiscountPercent, invoice.DiscountDueDate);
    }

    private async Task<SalesTaxRateResponseModel?> ResolveJurisdictionAsync(CreateInvoiceCommand request, CancellationToken ct)
    {
        if (request.SalesTaxRateId is int rateId)
        {
            return await db.SalesTaxRates.AsNoTracking()
                .Where(r => r.Id == rateId)
                .Select(r => new SalesTaxRateResponseModel(
                    r.Id, r.Name, r.Code, r.StateCode, r.Rate, r.EffectiveFrom, r.EffectiveTo,
                    r.IsDefault, r.IsActive, r.Description, r.ExemptFlag, r.GlPostingAccount, r.FreightTaxable))
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"Sales tax rate {rateId} not found");
        }

        var resolved = await GetTaxRateForCustomerHandler.ResolveAsync(db, request.CustomerId, request.InvoiceDate, ct);
        return resolved is not null && decimal.Round(resolved.Rate, 6) == decimal.Round(request.TaxRate, 6)
            ? resolved
            : null;
    }

    private async Task EnsureSalesOrderFreightNotBilledAsync(CreateInvoiceCommand request, CancellationToken ct)
    {
        var freightOrderLineIds = request.Lines
            .Where(l => l.LineType == DocumentLineType.Freight && l.SalesOrderLineId != null)
            .Select(l => l.SalesOrderLineId!.Value)
            .Distinct()
            .ToList();
        if (freightOrderLineIds.Count == 0)
            return;

        var billedOn = await db.InvoiceLines.AsNoTracking()
            .Where(il => il.SalesOrderLineId != null && freightOrderLineIds.Contains(il.SalesOrderLineId.Value)
                && il.Invoice.Status != InvoiceStatus.Voided)
            .Select(il => il.Invoice.InvoiceNumber)
            .FirstOrDefaultAsync(ct);
        if (billedOn is not null)
            throw FreightLines.Failure("Lines", FreightLines.SoFreightAlreadyBilledCode,
                $"This sales order's freight is already billed on invoice {billedOn}.");
    }

    private static string Qty(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);

    // Uses a caller-supplied invoice number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential number.
    private async Task<string> ResolveInvoiceNumberAsync(CreateInvoiceCommand request, CancellationToken ct)
    {
        var supplied = request.InvoiceNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualInvoiceNumbersAllowedAsync(ct))
        {
            if (await repo.InvoiceNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Invoice number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GenerateNextInvoiceNumberAsync(ct);
    }

    private async Task<bool> ManualInvoiceNumbersAllowedAsync(CancellationToken ct)
    {
        if (systemSettings is null) return false;
        var setting = await systemSettings.FindByKeyAsync(AllowManualInvoiceNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
