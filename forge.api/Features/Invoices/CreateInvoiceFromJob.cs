using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.SalesTax;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// Drafts the invoice for a completed job through <see cref="CreateInvoiceCommand"/>, so it gets
/// the same numbering, order link, shipped-quantity guard and sync as any other invoice. The
/// price comes from the job's sales-order line (that line's price and its not-yet-invoiced
/// quantity — what it shipped once anything has shipped, so an under- or over-shipment inside tolerance
/// bills what actually left, else what it ordered — at the order's tax rate) or, for a job with no order line, from the job's quoted
/// price at the customer's default tax rate. A job with neither is refused rather than drafted
/// at 0.00, because an invoice cannot be repriced after it is created. Credit terms come from the
/// order, then the customer's default, then Net 30, and the due date follows those terms.
/// </summary>
public record CreateInvoiceFromJobCommand(int JobId) : IRequest<InvoiceListItemModel>;

public class CreateInvoiceFromJobValidator : AbstractValidator<CreateInvoiceFromJobCommand>
{
    public CreateInvoiceFromJobValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
    }
}

public class CreateInvoiceFromJobHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<CreateInvoiceFromJobCommand, InvoiceListItemModel>
{
    private const CreditTerms FallbackTerms = CreditTerms.Net30;

    public async Task<InvoiceListItemModel> Handle(
        CreateInvoiceFromJobCommand request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Include(j => j.SalesOrderLine)
                .ThenInclude(l => l!.SalesOrder)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.CompletedDate == null)
            throw new InvalidOperationException("Cannot create an invoice for a job that is not completed");

        if (job.CustomerId is not int customerId)
            throw new InvalidOperationException("Cannot create an invoice for a job without a customer");

        var description = string.IsNullOrWhiteSpace(job.Description)
            ? $"{job.JobNumber} — {job.Title}"
            : $"{job.JobNumber} — {job.Title}: {job.Description}";
        var now = clock.UtcNow;
        var customerTerms = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.DefaultCreditTerms)
            .FirstOrDefaultAsync(cancellationToken);

        CreateInvoiceCommand command;
        if (job.SalesOrderLine is { } orderLine)
        {
            var order = orderLine.SalesOrder;
            var quantity = await UninvoicedQuantityAsync(orderLine, cancellationToken);
            if (quantity <= 0m)
                throw new InvalidOperationException(
                    $"Line {orderLine.LineNumber} of {order.OrderNumber} for {job.JobNumber} is already invoiced in full.");

            var terms = order.CreditTerms ?? customerTerms ?? FallbackTerms;
            command = new CreateInvoiceCommand(
                CustomerId: order.CustomerId,
                SalesOrderId: order.Id,
                ShipmentId: null,
                InvoiceDate: now,
                DueDate: now.AddDays(terms.NetDays()),
                CreditTerms: terms.ToString(),
                TaxRate: order.TaxRate,
                Notes: $"Invoice for {job.JobNumber}: {job.Title}",
                Lines:
                [
                    new CreateInvoiceLineModel(orderLine.PartId, description, quantity, orderLine.UnitPrice,
                        Taxable: orderLine.TaxCode == InvoiceLine.NonTaxableCode ? false : null, SalesOrderLineId: orderLine.Id),
                ]);
        }
        else if (job.QuotedPrice > 0m)
        {
            var taxRate = await mediator.Send(new GetTaxRateForCustomerQuery(customerId), cancellationToken);
            var terms = customerTerms ?? FallbackTerms;
            command = new CreateInvoiceCommand(
                CustomerId: customerId,
                SalesOrderId: null,
                ShipmentId: null,
                InvoiceDate: now,
                DueDate: now.AddDays(terms.NetDays()),
                CreditTerms: terms.ToString(),
                TaxRate: taxRate?.Rate ?? 0m,
                Notes: $"Invoice for {job.JobNumber}: {job.Title}",
                Lines: [new CreateInvoiceLineModel(job.PartId, description, 1m, job.QuotedPrice)]);
        }
        else
        {
            throw new InvalidOperationException(
                $"{job.JobNumber} has no price to invoice: link it to its sales-order line or give it a quoted price, "
                + "or create the invoice from the New invoice dialog.");
        }

        return await mediator.Send(command, cancellationToken);
    }

    private async Task<decimal> UninvoicedQuantityAsync(SalesOrderLine orderLine, CancellationToken ct)
    {
        if (orderLine.PartId is not int partId)
            return orderLine.Quantity;

        var invoicedForPart = await db.InvoiceLines
            .Where(l => l.PartId == partId && l.Invoice.SalesOrderId == orderLine.SalesOrderId
                && l.Invoice.Status != InvoiceStatus.Voided)
            .SumAsync(l => l.Quantity, ct);

        if (orderLine.ShippedQuantity > 0m)
        {
            var shippedForPart = await db.SalesOrderLines
                .Where(l => l.SalesOrderId == orderLine.SalesOrderId && l.PartId == partId)
                .SumAsync(l => l.ShippedQuantity, ct);
            return Math.Min(orderLine.ShippedQuantity, shippedForPart - invoicedForPart);
        }

        var orderedForPart = await db.SalesOrderLines
            .Where(l => l.SalesOrderId == orderLine.SalesOrderId && l.PartId == partId)
            .SumAsync(l => l.Quantity, ct);

        return Math.Min(orderLine.Quantity, orderedForPart - invoicedForPart);
    }
}
