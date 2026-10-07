using System.Globalization;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

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
    decimal FxRate = 1m,
    // Optional caller-supplied invoice number — see CreateInvoiceRequestModel.InvoiceNumber.
    string? InvoiceNumber = null) : IRequest<InvoiceListItemModel>;

public class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleFor(x => x.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate);
        RuleFor(x => x.CustomerPO).MaximumLength(50);
        RuleFor(x => x.InvoiceNumber).MaximumLength(20).When(x => !string.IsNullOrWhiteSpace(x.InvoiceNumber));
        // FX booking rate must be positive (a 0 or negative rate would zero/invert the functional amount).
        RuleFor(x => x.FxRate).GreaterThan(0m);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
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
    IAccountingService? accountingService = null,
    ISyncQueueRepository? syncQueue = null,
    // Optional / null-default (same reason): a manual-number override needs the setting repo, and the
    // identifier registry records the number for resolution/history. DI supplies both.
    ISystemSettingRepository? systemSettings = null,
    IBusinessIdentifierService? identifiers = null)
    : IRequestHandler<CreateInvoiceCommand, InvoiceListItemModel>
{
    // System setting that gates caller-supplied invoice numbers. Stored as "true"/"false".
    private const string AllowManualInvoiceNumbersKey = "invoices.allow_manual_numbers";

    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    public async Task<InvoiceListItemModel> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        string? salesOrderCustomerPo = null;
        if (request.SalesOrderId is int linkedSoId)
        {
            var salesOrder = await db.SalesOrders.AsNoTracking()
                .Where(so => so.Id == linkedSoId)
                .Select(so => new { so.OrderNumber, so.CustomerId, so.CustomerPO })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Sales order {linkedSoId} not found");

            if (salesOrder.CustomerId != request.CustomerId)
                throw new InvalidOperationException(
                    $"Sales order {salesOrder.OrderNumber} belongs to a different customer than {customer.Name}.");

            salesOrderCustomerPo = salesOrder.CustomerPO;
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

            var invoicedByPart = await db.InvoiceLines
                .Where(il => il.PartId != null && il.Invoice.SalesOrderId == shippedSoId)
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
                    $"{Qty(x.Shipped)} shipped, {Qty(x.Already)} already invoiced, " +
                    $"{Qty(Math.Max(0m, x.Shipped - x.Already))} left to invoice.")));
            }
        }

        var invoiceNumber = await ResolveInvoiceNumberAsync(request, cancellationToken);

        CreditTerms? creditTerms = request.CreditTerms != null
            ? Enum.Parse<CreditTerms>(request.CreditTerms, true)
            : null;

        // Propagate CustomerPO from the sourcing SO when the caller didn't
        // override. B2B customers reject invoices that don't echo their PO #.
        var customerPo = request.CustomerPO ?? salesOrderCustomerPo;

        // Multi-currency (Phase-4 FULLGL, additive). Resolve the invoice currency to the caller-supplied
        // CurrencyId, else the active book's functional currency (mirrors how the posting services load the
        // book). When no book is seeded (single-currency installs that never enabled the GL), fall back to the
        // seeded functional-currency default (1) — keeping the value functional and the row backward-compatible.
        var currencyId = request.CurrencyId ?? await ResolveFunctionalCurrencyIdAsync(db, cancellationToken);

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = request.CustomerId,
            CurrencyId = currencyId,
            FxRate = request.FxRate,
            SalesOrderId = request.SalesOrderId,
            ShipmentId = request.ShipmentId,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            CreditTerms = creditTerms,
            TaxRate = request.TaxRate,
            Notes = request.Notes,
            CustomerPO = customerPo,
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            invoice.Lines.Add(new InvoiceLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
            });
        }

        await repo.AddAsync(invoice, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Record the number in the identifier registry (history + resolution).
        if (identifiers is not null)
            await identifiers.IssueAsync(BusinessEntityType.Invoice, invoice.Id, invoice.InvoiceNumber, cancellationToken);

        var total = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + invoice.TaxRate);

        // AUDIT-21-S1 (BLOCKER): enqueue a QBO sync row so AR reaches the accounting provider —
        // previously only MoveJobStage enqueued, leaving invoices created any other way invisible to
        // sync. Only when a provider is actually connected (mirrors MoveJobStage); unlike the job path
        // we do NOT require the customer to be pre-synced — the sync worker upserts the customer, so a
        // first invoice for a brand-new customer still syncs.
        if (accountingService is not null && syncQueue is not null
            && await accountingService.TestConnectionAsync(cancellationToken))
        {
            var document = new AccountingDocument(
                Type: AccountingDocumentType.Invoice,
                CustomerExternalId: customer.ExternalId ?? string.Empty,
                LineItems: invoice.Lines
                    .Select(l => new AccountingLineItem(l.Description, l.Quantity, l.UnitPrice, ItemExternalId: null))
                    .ToList(),
                RefNumber: invoice.InvoiceNumber,
                Amount: total,
                Date: invoice.InvoiceDate);
            await syncQueue.EnqueueAsync("Invoice", invoice.Id, "CreateInvoice",
                JsonSerializer.Serialize(document), cancellationToken);
        }

        return new InvoiceListItemModel(
            invoice.Id, invoice.InvoiceNumber, invoice.CustomerId, customer.Name,
            invoice.Status.ToString(), invoice.InvoiceDate, invoice.DueDate,
            total, 0, total, invoice.CreatedAt);
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

    /// <summary>
    /// The active book's functional currency, or the seeded functional-currency default (1) when no book is
    /// present (single-currency installs that never enabled the GL). Read-only — never tracked.
    /// </summary>
    private static async Task<int> ResolveFunctionalCurrencyIdAsync(AppDbContext db, CancellationToken ct)
        => await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => b.FunctionalCurrencyId)
            .FirstOrDefaultAsync(ct) is int id and > 0 ? id : 1;
}
